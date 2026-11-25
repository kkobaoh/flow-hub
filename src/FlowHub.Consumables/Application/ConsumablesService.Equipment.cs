using FlowHub.Map;

namespace FlowHub.Consumables.Application;

/// <summary>
/// 消耗品の構成のうち、装置とステーションを、動いている間に加える・変える・消す（消耗品の構成の画面から）。
/// 名前（ID）は変えられない。受け渡しパネルは、公開中のマップの受け渡しパネルで、ほかの装置とステーションが使っていないものだけ。
/// </summary>
/// <remarks>
/// 公開中のマップの受け渡しパネル（特殊消耗品受け渡しパネルを含む）は、呼ぶ側（組み立て）がマップから読んで渡す。
/// 受け渡しパネルは、変えるときだけ確かめる（変えなければ、マップが変わっていても、ストッカーだけを変えられる）。
/// </remarks>
public sealed partial class ConsumablesService
{
    /// <summary>装置を加える。ストッカーの設定はまだないので、不足も回収もない装置として判断の対象になる。</summary>
    /// <exception cref="ArgumentException">ストッカーがないとき（頼みの誤り）。</exception>
    public Task<ConfigurationChangeResult> AddDeviceAsync(DeviceLayout layout, IReadOnlySet<PanelId> transferPanels, CancellationToken cancellationToken)
    {
        EnsureLayout(layout, transferPanels);
        return ChangeConfigurationAsync($"device:{layout.Id}", () =>
        {
            if (_devices.ContainsKey(layout.Id))
            {
                return ConfigurationChangeResult.AlreadyRegistered;
            }

            if (!IsPanelAvailable(layout.TransferPanel, transferPanels))
            {
                return ConfigurationChangeResult.PanelUnavailable;
            }

            _persistence.SaveDevice(layout);
            _devices[layout.Id] = layout;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);
    }

    /// <summary>
    /// 装置の受け渡しパネルと、ストッカーごとのスロットの数を変える。受け渡しパネルは、使われていないときだけ変えられる。
    /// 減らすストッカーとスロットは空のときだけで、減らしたあとのスロットの数がストッカーの設定の個数の合計を下回るなら断る。
    /// </summary>
    /// <exception cref="ArgumentException">ストッカーがないとき（頼みの誤り）。</exception>
    public Task<ConfigurationChangeResult> UpdateDeviceAsync(DeviceLayout layout, IReadOnlySet<PanelId> transferPanels, CancellationToken cancellationToken)
    {
        EnsureLayout(layout, transferPanels);
        return ChangeConfigurationAsync($"device:{layout.Id}", () =>
        {
            if (!_devices.TryGetValue(layout.Id, out var before))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (before.TransferPanel != layout.TransferPanel)
            {
                if (IsDeviceInUse(layout.Id))
                {
                    return ConfigurationChangeResult.InUse;
                }

                if (!IsPanelAvailable(layout.TransferPanel, transferPanels))
                {
                    return ConfigurationChangeResult.PanelUnavailable;
                }
            }

            var kept = layout.AllSlots.ToHashSet();
            if (OccupiedSlots(layout.Id).Any(slot => !kept.Contains(slot)))
            {
                return ConfigurationChangeResult.SlotsOccupied;
            }

            if (_settings.TryGetValue(layout.Id, out var counts) && counts.Values.Sum() > layout.SlotsPerStocker.Sum())
            {
                return ConfigurationChangeResult.ExceedsStockerSetting;
            }

            _persistence.SaveDevice(layout);
            _devices[layout.Id] = layout;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);
    }

    /// <summary>装置を消す（使われていないときだけ）。ストッカーの設定と段取り替えのエラーも一緒に消す。</summary>
    public Task<ConfigurationChangeResult> DeleteDeviceAsync(DeviceId device, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"device:{device}", () =>
        {
            if (!_devices.ContainsKey(device))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (IsDeviceInUse(device))
            {
                return ConfigurationChangeResult.InUse;
            }

            _persistence.DeleteDevice(device);
            _devices.Remove(device);
            _settings.Remove(device);
            _setupErrors.Remove(device);
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>ステーションを加える（ステーションの選び方のルールが決まるまでは、1台だけ）。</summary>
    public Task<ConfigurationChangeResult> AddStationAsync(StationLayout layout, IReadOnlySet<PanelId> transferPanels, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(transferPanels);
        return ChangeConfigurationAsync($"station:{layout.Id}", () =>
        {
            if (_stations.ContainsKey(layout.Id))
            {
                return ConfigurationChangeResult.AlreadyRegistered;
            }

            if (_stations.Count > 0)
            {
                return ConfigurationChangeResult.StationLimit;
            }

            if (!IsPanelAvailable(layout.TransferPanel, transferPanels))
            {
                return ConfigurationChangeResult.PanelUnavailable;
            }

            _persistence.SaveStation(layout);
            _stations[layout.Id] = layout;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);
    }

    /// <summary>ステーションの受け渡しパネルを変える（使われていないときだけ）。</summary>
    public Task<ConfigurationChangeResult> UpdateStationAsync(StationLayout layout, IReadOnlySet<PanelId> transferPanels, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(transferPanels);
        return ChangeConfigurationAsync($"station:{layout.Id}", () =>
        {
            if (!_stations.TryGetValue(layout.Id, out var before))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (before.TransferPanel == layout.TransferPanel)
            {
                return ConfigurationChangeResult.Saved;
            }

            if (IsStationInUse(layout.Id))
            {
                return ConfigurationChangeResult.InUse;
            }

            if (!IsPanelAvailable(layout.TransferPanel, transferPanels))
            {
                return ConfigurationChangeResult.PanelUnavailable;
            }

            _persistence.SaveStation(layout);
            _stations[layout.Id] = layout;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);
    }

    /// <summary>ステーションを消す（使われていないときだけ）。</summary>
    public Task<ConfigurationChangeResult> DeleteStationAsync(StationId station, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"station:{station}", () =>
        {
            if (!_stations.ContainsKey(station))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (IsStationInUse(station))
            {
                return ConfigurationChangeResult.InUse;
            }

            _persistence.DeleteStation(station);
            _stations.Remove(station);
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>画面に出す装置の構成（ロックの中で呼ぶ）。</summary>
    private DeviceConfigurationView[] DeviceConfigurations() =>
        _devices.Values
            .OrderBy(d => d.Id.Value, StringComparer.Ordinal)
            .Select(d => new DeviceConfigurationView(d.Id, d.TransferPanel, d.SlotsPerStocker.ToArray(), OccupiedSlots(d.Id).Order().ToArray(), IsDeviceInUse(d.Id)))
            .ToArray();

    /// <summary>画面に出すステーションの構成（ロックの中で呼ぶ）。</summary>
    private StationConfigurationView[] StationConfigurations() =>
        _stations.Values
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .Select(s => new StationConfigurationView(s.Id, s.TransferPanel, IsStationInUse(s.Id)))
            .ToArray();

    private static void EnsureLayout(DeviceLayout layout, IReadOnlySet<PanelId> transferPanels)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(transferPanels);
        if (layout.SlotsPerStocker.Count == 0)
        {
            throw new ArgumentException($"装置 {layout.Id} のストッカーを、1つ以上入れてください。", nameof(layout));
        }
    }

    /// <summary>公開中のマップの受け渡しパネルで、ほかの装置もステーションも使っていないか。</summary>
    private bool IsPanelAvailable(PanelId panel, IReadOnlySet<PanelId> transferPanels) =>
        transferPanels.Contains(panel)
        && _devices.Values.All(d => d.TransferPanel != panel)
        && _stations.Values.All(s => s.TransferPanel != panel);

    /// <summary>
    /// 装置が使われているか：その装置にある消耗品（ストッカー、加工部、受け渡し部、スロットにある所在不明）、
    /// その装置へ届ける予約と指名、許可を出して完了を待っている受け渡しのどれかがある。
    /// </summary>
    private bool IsDeviceInUse(DeviceId device)
    {
        ForgetInvalidDesignations();
        return _consumables.Values.Any(c => c.Location switch
            {
                InStocker s => s.Device == device,
                InProcessing p => p.Device == device,
                AtTransferUnit u => u.Device == device,
                UnknownLocation u => u.Device == device,
                _ => false,
            })
            || _deliveries.ContainsValue(device)
            || _designations.ContainsValue(device)
            || _pending.Values.Any(p => p.Device == device);
    }

    /// <summary>
    /// ステーションが使われているか：そのステーションにある消耗品、許可を出して完了を待っている受け渡し、
    /// AGV が積んでいる消耗品、届ける予約と回収の予約のどれかがある（ステーションは1台だけなので、届ける・回収する搬送はどれもこのステーションにかかわる）。
    /// </summary>
    private bool IsStationInUse(StationId station) =>
        _consumables.Values.Any(c => c.Location is AtStation at && at.Station == station || c.Location is OnAgv)
        || _pending.Values.Any(p => p.Station == station)
        || _deliveries.Count > 0
        || _recoveries.Count > 0;

    /// <summary>
    /// 装置のふさがっているスロット（減らせない）：消耗品があるスロット（所在不明でスロットがわかっているものを含む）、
    /// 届ける入れ先に決めたスロット、加工中の消耗品の元のスロット。
    /// </summary>
    private HashSet<SlotRef> OccupiedSlots(DeviceId device) =>
        _consumables.Values
            .Select(c => c.Location switch
            {
                InStocker s when s.Device == device => s.Slot,
                UnknownLocation u when u.Device == device => u.Slot,
                AtTransferUnit u when u.Device == device => u.PlannedSlot,
                InProcessing p when p.Device == device => p.HomeSlot,
                _ => null,
            })
            .OfType<SlotRef>()
            .Concat(_pending.Values.Where(p => p.Device == device).SelectMany(p => p.Slots.Values))
            .ToHashSet();
}
