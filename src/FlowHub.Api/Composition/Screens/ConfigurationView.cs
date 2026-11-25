using FlowHub.Consumables;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る、消耗品の構成（消耗品の構成の画面の、加工の種類、品種、装置、ステーション）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="ProcessTypes">加工の種類（ID の順）。</param>
/// <param name="Varieties">品種（ID の順）。</param>
/// <param name="Devices">装置（ID の順）。</param>
/// <param name="Stations">ステーション（ID の順）。</param>
internal sealed record ConfigurationSnapshot(
    long Version,
    IReadOnlyList<ProcessTypeConfigurationView> ProcessTypes,
    IReadOnlyList<VarietyView> Varieties,
    IReadOnlyList<DeviceConfigurationWire> Devices,
    IReadOnlyList<StationConfigurationWire> Stations)
{
    public static ConfigurationSnapshot From(long version, ConsumablesConfigurationView configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ConfigurationSnapshot(
            version,
            [.. configuration.ProcessTypes.Select(p => new ProcessTypeConfigurationView(p.Id.Value, p.Carrier.Value, p.CarrierCapacity))],
            [.. configuration.Varieties.Select(v => new VarietyView(
                v.Variety.Value,
                v.ProcessType.Value,
                [.. v.ExpectedIds.Select(id => id.Value)],
                [.. v.StockedIds.Select(id => id.Value)],
                v.InUse))],
            [.. configuration.Devices.Select(d => new DeviceConfigurationWire(
                d.Device.Value,
                d.TransferPanel.Value,
                [.. d.SlotsPerStocker],
                [.. d.OccupiedSlots.Select(s => new SlotView(s.Stocker, s.Slot))],
                d.InUse))],
            [.. configuration.Stations.Select(s => new StationConfigurationWire(s.Station.Value, s.TransferPanel.Value, s.InUse))]);
    }

    public bool Equals(ConfigurationSnapshot? other) =>
        other is not null
        && Version == other.Version
        && ProcessTypes.SequenceEqual(other.ProcessTypes)
        && Varieties.SequenceEqual(other.Varieties)
        && Devices.SequenceEqual(other.Devices)
        && Stations.SequenceEqual(other.Stations);

    public override int GetHashCode() => HashCode.Combine(Version, ProcessTypes.Count, Varieties.Count);
}

/// <summary>画面へ送る、加工の種類。</summary>
/// <param name="Carrier">キャリアの種類。</param>
/// <param name="Capacity">キャリアに入る消耗品の個数。</param>
internal sealed record ProcessTypeConfigurationView(string Id, string Carrier, int Capacity);

/// <summary>画面へ送る、品種。</summary>
/// <param name="Expected">入庫の予定の ID（外せる。ID の順）。</param>
/// <param name="Stocked">消耗品の記録にある ID（外せない。ID の順）。</param>
/// <param name="InUse">使われているか（使われていたら、加工の種類を変えられず、消せない）。</param>
internal sealed record VarietyView(string Id, string ProcessType, IReadOnlyList<string> Expected, IReadOnlyList<string> Stocked, bool InUse)
{
    public bool Equals(VarietyView? other) =>
        other is not null
        && Id == other.Id
        && ProcessType == other.ProcessType
        && Expected.SequenceEqual(other.Expected)
        && Stocked.SequenceEqual(other.Stocked)
        && InUse == other.InUse;

    public override int GetHashCode() => HashCode.Combine(Id, ProcessType, InUse);
}

/// <summary>画面へ送る、装置の構成。</summary>
/// <param name="Panel">受け渡しパネル。</param>
/// <param name="Slots">ストッカーごとのスロットの数。</param>
/// <param name="Occupied">ふさがっているスロット（減らせない）。</param>
/// <param name="InUse">使われているか（使われていたら、受け渡しパネルを変えられず、消せない）。</param>
internal sealed record DeviceConfigurationWire(string Id, string Panel, IReadOnlyList<int> Slots, IReadOnlyList<SlotView> Occupied, bool InUse)
{
    public bool Equals(DeviceConfigurationWire? other) =>
        other is not null && Id == other.Id && Panel == other.Panel && Slots.SequenceEqual(other.Slots) && Occupied.SequenceEqual(other.Occupied) && InUse == other.InUse;

    public override int GetHashCode() => HashCode.Combine(Id, Panel, InUse);
}


/// <summary>画面へ送る、ステーションの構成。</summary>
/// <param name="Panel">受け渡しパネル。</param>
/// <param name="InUse">使われているか（使われていたら、受け渡しパネルを変えられず、消せない）。</param>
internal sealed record StationConfigurationWire(string Id, string Panel, bool InUse);
