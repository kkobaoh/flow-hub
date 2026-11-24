using FlowHub.Consumables;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る装置とステーションの様子（気をつけること、ストッカー、在庫）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
internal sealed record EquipmentStatusSnapshot(long Version, IReadOnlyList<DeviceStatusView> Devices, IReadOnlyList<StationStatusView> Stations)
{
    public static EquipmentStatusSnapshot From(long version, ConsumablesStatuses statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return new EquipmentStatusSnapshot(version, [.. statuses.Devices.Select(DeviceStatusView.From)], [.. statuses.Stations.Select(StationStatusView.From)]);
    }
}

/// <summary>画面へ送る装置の様子（中身の意味は <see cref="DeviceStatus"/>）。</summary>
internal sealed record DeviceStatusView(
    string Id,
    int Shortage,
    int CollectWaiting,
    IReadOnlyList<SlotView> MismatchedSlots,
    IReadOnlyList<string> SetupErrors,
    IReadOnlyList<StockerLineView> Stocker,
    SlotSummaryView Slots)
{
    public static DeviceStatusView From(DeviceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new DeviceStatusView(
            status.Device.Value,
            status.Shortage,
            status.CollectWaiting,
            [.. status.MismatchedSlots.Select(s => new SlotView(s.Stocker, s.Slot))],
            [.. status.SetupErrors.Select(v => v.Value)],
            [.. status.Stocker.Select(l => new StockerLineView(l.Variety.Value, l.Target, l.Have))],
            new SlotSummaryView(status.Slots.Total, status.Slots.Free, status.Slots.Delivering));
    }

    public bool Equals(DeviceStatusView? other) =>
        other is not null
        && Id == other.Id
        && Shortage == other.Shortage
        && CollectWaiting == other.CollectWaiting
        && MismatchedSlots.SequenceEqual(other.MismatchedSlots)
        && SetupErrors.SequenceEqual(other.SetupErrors)
        && Stocker.SequenceEqual(other.Stocker)
        && Slots == other.Slots;

    public override int GetHashCode() => HashCode.Combine(Id, Shortage, CollectWaiting, Slots);
}

/// <summary>ストッカーのスロット（ストッカーの番号とスロットの番号。どちらも 1 から）。</summary>
internal sealed record SlotView(int Stocker, int Slot);

internal sealed record StockerLineView(string Variety, int Target, int Have);

internal sealed record SlotSummaryView(int Total, int Free, int Delivering);

/// <summary>画面へ送るステーションの様子（中身の意味は <see cref="StationStatus"/>）。</summary>
internal sealed record StationStatusView(string Id, int NeedsCheck, int ReplenishWaiting, IReadOnlyList<StationLineView> Stock)
{
    public static StationStatusView From(StationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new StationStatusView(
            status.Station.Value,
            status.NeedsCheck,
            status.ReplenishWaiting,
            [.. status.Stock.Select(l => new StationLineView(l.Variety.Value, l.Available, l.ReplenishWaiting))]);
    }

    public bool Equals(StationStatusView? other) =>
        other is not null && Id == other.Id && NeedsCheck == other.NeedsCheck && ReplenishWaiting == other.ReplenishWaiting && Stock.SequenceEqual(other.Stock);

    public override int GetHashCode() => HashCode.Combine(Id, NeedsCheck, ReplenishWaiting);
}

internal sealed record StationLineView(string Variety, int Available, int ReplenishWaiting);
