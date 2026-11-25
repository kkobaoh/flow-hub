namespace FlowHub.Consumables.Application;

/// <summary>
/// 在庫の手入力（消耗品の検索の画面からの、登録・直す・消す）。
/// 人が記録を現物に合わせるための操作なので、直したら要確認を外し、付いていた予約と指名を解く。
/// </summary>
/// <remarks>
/// AGV を割り当て済みの搬送の予約が付いているかは、搬送の要求を持つ搬送計画が確かめてから呼ぶ。
/// 補充の入庫には数えない（補充の入庫は、ステーションが ID を読み取ったときだけ数える）。
/// </remarks>
public sealed partial class ConsumablesService
{
    /// <summary>
    /// 消耗品を手入力で登録する。所在は、ステーションか、装置のストッカーの空いているスロット。
    /// ステーションに登録したら、入庫した日時は登録した日時にする。入庫の予定にあった ID なら、予定から外す。
    /// </summary>
    /// <exception cref="ArgumentException">所在がステーションでもストッカーのスロットでもないとき（頼みの誤り）。</exception>
    /// <exception cref="InvalidOperationException">知らない品種、装置、ステーションのとき（頼みの誤り）。</exception>
    public async Task<ManualEntryResult> RegisterManuallyAsync(
        ConsumableId id, VarietyId variety, ConsumableCondition condition, ConsumableLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var result = await LockedAsync(() =>
        {
            var processType = VarietyProcessType(variety);
            EnsureManualLocation(location);
            if (_consumables.ContainsKey(id))
            {
                return ManualEntryResult.AlreadyRegistered;
            }

            if (IsSlotTaken(location, except: null))
            {
                return ManualEntryResult.SlotTaken;
            }

            if (_expected.Remove(id))
            {
                _persistence.RemoveExpectedArrival(id);
            }

            _consumables[id] = new ConsumableView(id, variety, processType, condition, location);
            NoteArrival(id, location);
            return ManualEntryResult.Saved;
        }, cancellationToken).ConfigureAwait(false);

        await PublishIfSavedAsync(result, id, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 消耗品の記録を手入力で直す（品種、新品か中古品か、所在）。要確認を外し、付いていた予約と指名を解く。
    /// ステーションのまま直したときは入庫した日時を変えず、ほかからステーションへ直したときは直した日時にする。
    /// </summary>
    public async Task<ManualEntryResult> EditManuallyAsync(
        ConsumableId id, VarietyId variety, ConsumableCondition condition, ConsumableLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var result = await LockedAsync(() =>
        {
            var processType = VarietyProcessType(variety);
            EnsureManualLocation(location);
            if (EditRefusal(id) is { } refusal)
            {
                return refusal;
            }

            if (IsSlotTaken(location, except: id))
            {
                return ManualEntryResult.SlotTaken;
            }

            var before = _consumables[id];
            _consumables[id] = before with { Variety = variety, ProcessType = processType, Condition = condition, Location = location, Check = NeedsCheckReason.NotNeeded };
            ReleaseReservations(id);
            _designations.Remove(id);
            _arrivedUnregistered.Remove(id);
            if (before.Location != location)
            {
                _arrivals.Remove(id);
                NoteArrival(id, location);
            }

            return ManualEntryResult.Saved;
        }, cancellationToken).ConfigureAwait(false);

        await PublishIfSavedAsync(result, id, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 消耗品の記録を手入力で消す。付いていた予約と指名を解く。
    /// </summary>
    public async Task<ManualEntryResult> DeleteManuallyAsync(ConsumableId id, CancellationToken cancellationToken)
    {
        var result = await LockedAsync(() =>
        {
            if (EditRefusal(id) is { } refusal)
            {
                return refusal;
            }

            ReleaseReservations(id);
            _designations.Remove(id);
            _arrivals.Remove(id);
            _arrivedUnregistered.Remove(id);
            _consumables.Remove(id);
            return ManualEntryResult.Saved;
        }, cancellationToken).ConfigureAwait(false);

        await PublishIfSavedAsync(result, id, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 直したり消したりできない理由。記録にない、AGV の中・加工部・受け渡し部にある、出庫を頼んだもの。できるなら null。
    /// </summary>
    private ManualEntryResult? EditRefusal(ConsumableId id)
    {
        if (!_consumables.TryGetValue(id, out var consumable))
        {
            return ManualEntryResult.NotFound;
        }

        return consumable.Location is OnAgv or InProcessing or AtTransferUnit || _shipping.Contains(id)
            ? ManualEntryResult.NotEditable
            : null;
    }

    /// <summary>手入力で選べる所在（ステーションか、装置のストッカーのスロット）か確かめる。</summary>
    private void EnsureManualLocation(ConsumableLocation location)
    {
        switch (location)
        {
            case AtStation at:
                Station(at.Station);
                break;
            case InStocker stocker:
                EnsureSlot(stocker.Device, stocker.Slot);
                break;
            default:
                throw new ArgumentException($"手入力で選べる所在は、ステーションか、装置のストッカーのスロットです（{location}）。", nameof(location));
        }
    }

    /// <summary>
    /// そのスロットが、ほかの消耗品でふさがっているか（ストッカーにあるもの、現物がスロットにある所在不明のもの、届ける入れ先に決めたもの）。
    /// 取り込み中の元のスロットは、空きとして扱う。
    /// </summary>
    private bool IsSlotTaken(ConsumableLocation location, ConsumableId? except)
    {
        if (location is not InStocker target)
        {
            return false;
        }

        var occupied = _consumables.Values
            .Where(c => c.Id != except)
            .Any(c => c.Location switch
            {
                InStocker s => s.Device == target.Device && s.Slot == target.Slot,
                UnknownLocation u => u.Device == target.Device && u.Slot == target.Slot,
                AtTransferUnit u => u.Device == target.Device && u.PlannedSlot == target.Slot,
                _ => false,
            });
        var planned = _pending.Values.Any(p => p.Device == target.Device && p.Side == Dispatching.TransferSide.Unload && p.Slots.ContainsValue(target.Slot));
        return occupied || planned;
    }

    private Task PublishIfSavedAsync(ManualEntryResult result, ConsumableId id, CancellationToken cancellationToken) =>
        result == ManualEntryResult.Saved ? PublishStockChangedAsync($"manual:{id}", cancellationToken) : Task.CompletedTask;
}
