namespace FlowHub.Consumables.Application;

/// <summary>
/// ステーションまわり（ID の読み取りによる入庫、要確認の修正、出庫）。
/// ステーションは ID しか読めないので、品種は前もって登録された ID と品種の対応から決める。
/// </summary>
public sealed partial class ConsumablesService
{
    /// <summary>
    /// 入庫する予定の消耗品の ID と品種の対応を、前もって登録する（上位システムか人が行う）。
    /// </summary>
    public void RegisterExpectedArrival(ConsumableId id, VarietyId variety) =>
        Locked(() =>
        {
            VarietyProcessType(variety);
            if (_consumables.ContainsKey(id))
            {
                throw new InvalidOperationException($"消耗品 {id} はすでに登録されています。");
            }

            Add(_expected, id, variety, "入庫の予定");
            _persistence.AddExpectedArrival(id, variety);
        });

    /// <summary>
    /// ステーションが、入ってきた消耗品の ID を読み取った。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>前もって品種の対応が登録されていれば、その品種の新品として入庫する。その品種の、補充を頼んでまだ入庫されていない数を減らす。</item>
    /// <item>未登録で対応もなければ、品種のわからない要確認の消耗品として登録する。</item>
    /// <item>記録上所在不明なら、所在を確定させて要確認にする。</item>
    /// <item>記録上ほかの場所にあれば、通知を正としてステーションに置き、予約を解いて食い違いとして返す。</item>
    /// </list>
    /// </remarks>
    public async Task<NotificationReport> ReportReadAtStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken)
    {
        var report = await LockedAsync(() =>
        {
            Station(station);
            var location = new AtStation(station);
            if (!_consumables.TryGetValue(id, out var consumable))
            {
                RegisterArrival(id, location);
                return new NotificationReport([]);
            }

            if (consumable.Location == location)
            {
                return new NotificationReport([]); // 同じ読み取りがもう一度届いた
            }

            var lost = consumable.Location is UnknownLocation;
            _consumables[id] = consumable with { Location = location, NeedsCheck = consumable.NeedsCheck || lost };
            NoteArrival(id, location);
            ReleaseReservations(id);
            return new NotificationReport(lost ? [] : [id]);
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"station:{station}", cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// 人がステーションで確かめて、要確認の消耗品の品種と、新品か中古品かを直した。要確認を外す。
    /// 未登録の ID として入庫した消耗品を新品と直したときは、品種の対応なしに届いた補充とみなし、入庫に数える。
    /// </summary>
    public async Task CorrectAtStationAsync(ConsumableId id, VarietyId variety, ConsumableCondition condition, CancellationToken cancellationToken)
    {
        var station = await LockedAsync(() =>
        {
            var consumable = Consumable(id);
            if (consumable.Location is not AtStation at || !consumable.NeedsCheck)
            {
                throw new InvalidOperationException($"消耗品 {id} は、ステーションにある要確認の消耗品ではありません。");
            }

            _consumables[id] = consumable with
            {
                Variety = variety,
                ProcessType = VarietyProcessType(variety),
                Condition = condition,
                NeedsCheck = false,
            };
            if (_arrivedUnregistered.Remove(id) && condition == ConsumableCondition.New)
            {
                NoteReplenished(variety);
            }

            return at.Station;
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"station:{station}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ステーションに、消耗品の出庫を頼む（上位システムの指示）。出るまで、届ける候補にしない。
    /// 届ける予約が付いているかどうかは、搬送の要求を持つ搬送計画が確かめてから呼ぶ。
    /// </summary>
    /// <returns>受け付けたら true。ステーションにない消耗品なら false。</returns>
    public async Task<bool> RequestShipmentAsync(ConsumableId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Consumable(id).Location is not AtStation at)
            {
                return false;
            }

            _shipping.Add(id);
            await stations.RequestShipmentAsync(at.Station, id, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// ステーションから消耗品が出た（人が任意に出したか、頼んだ出庫）。記録から消し、付いていた予約を解く。
    /// 記録上そのステーションになかったり、未登録だったりしたら、食い違いとして返す。
    /// </summary>
    public async Task<NotificationReport> ReportShippedFromStationAsync(StationId station, ConsumableId id, CancellationToken cancellationToken)
    {
        var report = await LockedAsync(() =>
        {
            Station(station);
            if (!_consumables.Remove(id, out var consumable))
            {
                return new NotificationReport([id]);
            }

            ReleaseReservations(id);
            _arrivalOrder.Remove(id);
            _arrivedUnregistered.Remove(id);
            return new NotificationReport(consumable.Location == new AtStation(station) ? [] : [id]);
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"station:{station}", cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// 未登録の ID を入庫する。品種の対応があればその品種の新品、なければ品種のわからない要確認の消耗品にする。
    /// </summary>
    private void RegisterArrival(ConsumableId id, AtStation location)
    {
        if (_expected.Remove(id, out var variety))
        {
            _persistence.RemoveExpectedArrival(id);
            _consumables[id] = new ConsumableView(id, variety, VarietyProcessType(variety), ConsumableCondition.New, location);
        }
        else
        {
            _consumables[id] = new ConsumableView(id, null, null, ConsumableCondition.New, location, NeedsCheck: true);
        }

        NoteArrival(id, location);
        if (_consumables[id].Variety is { } arrived)
        {
            NoteReplenished(arrived);
        }
        else
        {
            _arrivedUnregistered.Add(id);
        }
    }

    /// <summary>
    /// 補充を頼んだ品種が1個入庫した。まだ入庫されていない数を1つ減らす。
    /// </summary>
    private void NoteReplenished(VarietyId variety)
    {
        if (!_outstanding.TryGetValue(variety, out var outstanding))
        {
            return;
        }

        if (outstanding > 1)
        {
            _outstanding[variety] = outstanding - 1;
        }
        else
        {
            _outstanding.Remove(variety);
        }
    }

    private StationLayout Station(StationId station) =>
        _stations.TryGetValue(station, out var layout)
            ? layout
            : throw new InvalidOperationException($"ステーション {station} は登録されていません。");
}
