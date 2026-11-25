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
        var varieties = _varieties.Keys.OrderBy(v => v.Value, StringComparer.Ordinal).ToArray();
        return new ConsumablesStatuses(devices, stations, varieties);
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
        var needed = needs.Recoveries.ToDictionary(r => r.Consumable, r => r.Reason);
        HeldConsumable Held(ConsumableView c) => new(
            c.Id,
            c.Variety,
            c.Condition,
            Mismatched: c.Location is UnknownLocation,
            DiscardRequested: _discards.ContainsKey(c.Id),
            _recoveries.Contains(c.Id) ? new RecoveryRequested() : needed.TryGetValue(c.Id, out var reason) ? new RecoveryNeeded(reason) : new NotRecovering());
        HeldConsumable[] HeldWhere(Func<ConsumableLocation, bool> where) =>
            _consumables.Values.Where(c => where(c.Location)).OrderBy(c => c.Id.Value, StringComparer.Ordinal).Select(Held).ToArray();

        var slots = Layout(device).AllSlots
            .Select(slot => new StockerSlot(
                slot,
                atDevice.FirstOrDefault(c => SlotOfLocation(c.Location) == slot) is { } here ? new OccupiedSlot(Held(here)) : new EmptySlot()))
            .ToArray();

        return new DeviceStatus(
            device,
            needs.Shortages.Values.Sum(),
            collectWaiting,
            mismatched,
            setupErrors,
            stocker,
            new SlotSummary(Layout(device).SlotsPerStocker.Sum(), needs.FreeSlots, reserved.Length),
            Processing: HeldWhere(l => l is InProcessing p && p.Device == device),
            StockerSlots: slots,
            Handover: HeldWhere(l => l is AtTransferUnit u && u.Device == device),
            TransferPanel: Layout(device).TransferPanel);
    }

    /// <summary>ストッカーにあるもの、現物がスロットにある所在不明のもののスロット。</summary>
    private static SlotRef? SlotOfLocation(ConsumableLocation location) => location switch
    {
        InStocker s => s.Slot,
        UnknownLocation u => u.Slot,
        _ => null,
    };

    private StationStatus StationStatusOf(StationId station)
    {
        var here = _consumables.Values.Where(c => c.Location is AtStation s && s.Station == station).ToArray();
        var available = here
            .Where(c => IsDeliverable(c) && !_deliveries.ContainsKey(c.Id) && !_designations.ContainsKey(c.Id))
            .GroupBy(c => c.Variety.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => g.Count());
        var reserved = here
            .Where(c => _deliveries.ContainsKey(c.Id) && c.Variety is not null)
            .GroupBy(c => c.Variety.GetValueOrDefault())
            .ToDictionary(g => g.Key, g => g.Count());
        var stock = _varieties.Keys
            .OrderBy(v => v.Value, StringComparer.Ordinal)
            .Select(v => new StationLine(v, available.GetValueOrDefault(v), reserved.GetValueOrDefault(v), _outstanding.GetValueOrDefault(v)))
            .ToArray();
        var items = here
            .OrderBy(c => c.Id.Value, StringComparer.Ordinal)
            .Select(c => new StationItem(c.Id, c.Variety, c.Condition, _arrivals[c.Id].At, StationItemStateOf(c)))
            .ToArray();
        return new StationStatus(station, here.Count(c => c.NeedsCheck), _outstanding.Values.Sum(), stock, items, Station(station).TransferPanel);
    }

    /// <summary>ステーションにある消耗品の状態（要確認、出庫を頼んだ、届ける予約、指名、の順に1つ）。</summary>
    private StationItemState StationItemStateOf(ConsumableView consumable)
    {
        if (consumable.NeedsCheck)
        {
            return new CheckNeeded(consumable.Check);
        }

        if (_shipping.Contains(consumable.Id))
        {
            return new ShippingRequested();
        }

        if (_deliveries.TryGetValue(consumable.Id, out var reservedFor))
        {
            return new ReservedForDelivery(reservedFor);
        }

        if (_designations.TryGetValue(consumable.Id, out var designatedFor))
        {
            return new DesignatedFor(designatedFor);
        }

        return IsDeliverable(consumable) ? new Deliverable() : new NotDeliverable();
    }
}
