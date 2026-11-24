namespace FlowHub.Consumables.Application;

/// <summary>
/// 画面に出す、装置とステーションの様子（気をつけること、ストッカー、在庫）。
/// </summary>
public sealed partial class ConsumablesService
{
    /// <summary>
    /// 段取り替えのエラーになった品種（装置ごと）。設定を変えるか、次のリクエストが通ったら消す。
    /// DB には残さない（起動し直すと消える。上位システムと画面のメッセージには知らせてある）。
    /// </summary>
    private readonly Dictionary<DeviceId, HashSet<VarietyId>> _setupErrors = [];

    /// <summary>装置とステーションの、今の様子。</summary>
    public ConsumablesStatuses ReadStatuses() => Locked(() =>
    {
        ForgetInvalidDesignations();
        var devices = _devices.Keys.OrderBy(d => d.Value, StringComparer.Ordinal).Select(DeviceStatusOf).ToArray();
        var stations = _stations.Keys.OrderBy(s => s.Value, StringComparer.Ordinal).Select(StationStatusOf).ToArray();
        return new ConsumablesStatuses(devices, stations);
    });

    private DeviceStatus DeviceStatusOf(DeviceId device)
    {
        var needs = Evaluate(device);
        var atDevice = _consumables.Values.Where(c => c.Location switch
        {
            InStocker s => s.Device == device,
            UnknownLocation { Slot: not null } u => u.Device == device,
            _ => false,
        }).ToArray();

        // 回収待ち：回収が要るがまだ頼んでいないもの（判断の結果）と、回収を頼んでまだ取り出していないもの
        var collectWaiting = needs.Recoveries.Count + atDevice.Count(c => _recoveries.Contains(c.Id));

        var mismatched = atDevice
            .Select(c => c.Location)
            .OfType<UnknownLocation>()
            .Select(u => u.Slot.GetValueOrDefault())
            .Distinct()
            .Order()
            .ToArray();

        // ある新品：ストッカーにある新品と、届ける途中のもの（届ける予約をしたものと、受け渡し部にある新品）
        var reserved = _deliveries.Where(pair => pair.Value == device).Select(pair => _consumables[pair.Key]).ToArray();
        var have = atDevice
            .Where(c => c.Location is InStocker && c.Condition == ConsumableCondition.New)
            .Concat(reserved)
            .Concat(_consumables.Values.Where(c => c.Location is AtTransferUnit u && u.Device == device && c.Condition == ConsumableCondition.New))
            .Where(c => c.Variety is not null)
            .GroupBy(c => c.Variety.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => g.Count());
        var setting = _settings.GetValueOrDefault(device) ?? new Dictionary<VarietyId, int>();
        var stocker = setting.Keys.Union(have.Keys)
            .OrderBy(v => v.Value, StringComparer.Ordinal)
            .Select(v => new StockerLine(v, setting.GetValueOrDefault(v), have.GetValueOrDefault(v)))
            .ToArray();

        var setupErrors = _setupErrors.TryGetValue(device, out var errors) ? errors.OrderBy(v => v.Value, StringComparer.Ordinal).ToArray() : [];
        return new DeviceStatus(
            device,
            needs.Shortages.Values.Sum(),
            collectWaiting,
            mismatched,
            setupErrors,
            stocker,
            new SlotSummary(Layout(device).SlotsPerStocker.Sum(), needs.FreeSlots, reserved.Length));
    }

    private StationStatus StationStatusOf(StationId station)
    {
        var here = _consumables.Values.Where(c => c.Location is AtStation s && s.Station == station).ToArray();
        var available = here
            .Where(c => IsDeliverable(c) && !_deliveries.ContainsKey(c.Id) && !_designations.ContainsKey(c.Id))
            .GroupBy(c => c.Variety.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => g.Count());
        var stock = _varieties.Keys
            .OrderBy(v => v.Value, StringComparer.Ordinal)
            .Select(v => new StationLine(v, available.GetValueOrDefault(v), _outstanding.GetValueOrDefault(v)))
            .ToArray();
        return new StationStatus(station, here.Count(c => c.NeedsCheck), _outstanding.Values.Sum(), stock);
    }
}
