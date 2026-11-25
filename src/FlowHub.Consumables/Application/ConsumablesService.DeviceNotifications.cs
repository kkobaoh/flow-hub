namespace FlowHub.Consumables.Application;

/// <summary>
/// 装置からの通知（加工部への取り込み、ストッカーに戻した、照合のエラー、交換の完了、段取り替えのリクエスト）。
/// 装置が現物を見て知らせてくるので、記録と食い違っても通知を正として所在を更新する。
/// </summary>
public sealed partial class ConsumablesService
{
    /// <summary>加工の種類の設定。</summary>
    public ProcessTypeSettings GetProcessType(ProcessTypeId id) => Locked(() => ProcessType(id));

    /// <summary>
    /// 装置が、消耗品を加工部に取り込んだ（照合は装置で済んでいる）。中古品にして、元のスロットを覚える。
    /// </summary>
    public async Task<NotificationReport> ReportTakenIntoProcessingAsync(DeviceId device, ConsumableId id, CancellationToken cancellationToken)
    {
        var report = await LockedAsync(() =>
        {
            Layout(device);
            var consumable = Consumable(id);
            var home = consumable.Location is InStocker stocker && stocker.Device == device ? stocker.Slot : (SlotRef?)null;

            _consumables[id] = consumable with { Condition = ConsumableCondition.Used, Location = new InProcessing(device, home) };
            ReleaseReservations(id);
            return new NotificationReport(home is null ? [id] : []);
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"device:{device}", cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// 装置が、使い終わった中古品をストッカーに戻した（戻すスロットは装置が決める）。
    /// 戻したスロットに記録上別の消耗品があれば、その消耗品を所在不明にする。
    /// </summary>
    public async Task<NotificationReport> ReportReturnedToStockerAsync(DeviceId device, ConsumableId id, SlotRef slot, CancellationToken cancellationToken)
    {
        var report = await LockedAsync(() =>
        {
            EnsureSlot(device, slot);
            var consumable = Consumable(id);
            var mismatched = new List<ConsumableId>();
            if (consumable.Location is not InProcessing processing || processing.Device != device)
            {
                mismatched.Add(id);
            }

            foreach (var other in AtSlot(device, slot).Where(c => c.Id != id).ToArray())
            {
                MarkLost(other.Id);
                mismatched.Add(other.Id);
            }

            _consumables[id] = _consumables[id] with { Location = new InStocker(device, slot) };
            return new NotificationReport(mismatched);
        }, cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"device:{device}", cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// 装置が、取り込もうとした消耗品の ID の照合が合わなかったと知らせてきた。
    /// 記録上そのスロットにあった消耗品と、読み取った消耗品を、どちらも所在不明にする。
    /// 読み取った消耗品は現物がスロットにあるので、人の確認が要るものとして、そのスロットにあるとする（不整合として回収する）。
    /// </summary>
    /// <param name="readId">装置が読み取った ID。未登録なら、品種のわからない消耗品として登録する。</param>
    public async Task ReportIdMismatchAsync(DeviceId device, SlotRef slot, ConsumableId readId, CancellationToken cancellationToken)
    {
        await LockedAsync(() => ReplaceAtSlot(device, slot, readId), cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"device:{device}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 人が、照合が合わなかった消耗品を交換した。取り出された消耗品は所在不明にする。
    /// 交換後の消耗品も、人の確認が要るものとして、そのスロットにあるとする（不整合として回収する）。
    /// </summary>
    /// <param name="newId">交換後の消耗品の ID。未登録なら、品種のわからない消耗品として登録する。</param>
    public async Task ReportReplacedAsync(DeviceId device, SlotRef slot, ConsumableId newId, CancellationToken cancellationToken)
    {
        await LockedAsync(() => ReplaceAtSlot(device, slot, newId), cancellationToken).ConfigureAwait(false);

        await PublishStockChangedAsync($"device:{device}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 装置から、段取り替えのリクエストが来た。品種がストッカーの設定に含まれて（1個以上）いなければ、上位システムにエラーを知らせる。
    /// </summary>
    public Task ReportSetupRequestedAsync(DeviceId device, VarietyId variety, CancellationToken cancellationToken) =>
        LockedAsync(() =>
        {
            Layout(device);
            var included = _settings.TryGetValue(device, out var counts) && counts.GetValueOrDefault(variety) >= 1;
            if (included)
            {
                _setupErrors.Remove(device); // リクエストが通ったら、前のエラーは片付いたものとする
                return Task.CompletedTask;
            }

            if (!_setupErrors.TryGetValue(device, out var errors))
            {
                errors = [];
                _setupErrors[device] = errors;
            }

            errors.Add(variety);
            return upstream.NotifySetupErrorAsync(device, variety, cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// スロットにあると記録されていたものを所在不明にし、現物（<paramref name="actual"/>）を、人の確認が要るものとしてそのスロットに置く。
    /// </summary>
    private void ReplaceAtSlot(DeviceId device, SlotRef slot, ConsumableId actual)
    {
        EnsureSlot(device, slot);
        var recorded = AtSlot(device, slot).ToArray();
        var processType = _consumables.TryGetValue(actual, out var known)
            ? known.ProcessType
            : ProcessTypeForUnknown(device, recorded);

        foreach (var lost in recorded.Where(c => c.Id != actual))
        {
            MarkLost(lost.Id);
        }

        var location = new UnknownLocation(device, slot);
        _consumables[actual] = known is not null
            ? known with { Location = location, Check = NeedsCheckReason.IdMismatch }
            : new ConsumableView(actual, null, processType, ConsumableCondition.New, location, NeedsCheckReason.IdMismatch);
        ReleaseReservations(actual);
    }

    /// <summary>
    /// 未登録の消耗品の加工の種類。記録上そのスロットにあった消耗品の加工の種類を使う。
    /// 記録上何もなければ、装置の設定にある加工の種類が1つだけならそれを使い、そうでなければエラーにする。
    /// </summary>
    private ProcessTypeId ProcessTypeForUnknown(DeviceId device, ConsumableView[] recorded)
    {
        if (recorded.Select(c => c.ProcessType).FirstOrDefault(p => p is not null) is { } known)
        {
            return known;
        }

        var processTypes = _settings.TryGetValue(device, out var counts)
            ? counts.Keys.Select(VarietyProcessType).Distinct().ToArray()
            : [];
        return processTypes.Length == 1
            ? processTypes[0]
            : throw new InvalidOperationException($"装置 {device} の加工の種類が1つに決まらないので、未登録の消耗品をどのキャリアで運ぶか決められません。");
    }

    /// <summary>そのスロットにあると記録されている消耗品（ストッカーにあるものと、現物がスロットにある所在不明のもの）。</summary>
    private IEnumerable<ConsumableView> AtSlot(DeviceId device, SlotRef slot) =>
        _consumables.Values.Where(c => c.Location == new InStocker(device, slot) || c.Location == new UnknownLocation(device, slot));

    /// <summary>所在不明にし、付いていた予約を解く。</summary>
    private void MarkLost(ConsumableId id)
    {
        _consumables[id] = _consumables[id] with { Location = new UnknownLocation() };
        ReleaseReservations(id);
    }

    /// <summary>届ける・回収する予約と、廃棄リクエストを外す。</summary>
    private void ReleaseReservations(ConsumableId id)
    {
        _deliveries.Remove(id);
        _recoveries.Remove(id);
        _discards.Remove(id);
        _shipping.Remove(id);
    }

    private void EnsureSlot(DeviceId device, SlotRef slot)
    {
        if (!Layout(device).AllSlots.Contains(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"装置 {device} に、そのスロットはありません。");
        }
    }
}
