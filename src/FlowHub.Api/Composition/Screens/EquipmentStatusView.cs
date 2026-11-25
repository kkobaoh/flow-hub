using FlowHub.Consumables;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る装置とステーションの様子（気をつけること、ストッカー、在庫、装置の中、ステーションにある消耗品）。変わるたびに番号を進める。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
/// <param name="Varieties">登録してある品種（ID の順）。</param>
internal sealed record EquipmentStatusSnapshot(long Version, IReadOnlyList<DeviceStatusView> Devices, IReadOnlyList<StationStatusView> Stations, IReadOnlyList<string> Varieties)
{
    public static EquipmentStatusSnapshot From(long version, ConsumablesStatuses statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return new EquipmentStatusSnapshot(version, [.. statuses.Devices.Select(DeviceStatusView.From)], [.. statuses.Stations.Select(StationStatusView.From)], [.. statuses.Varieties.Select(v => v.Value)]);
    }
}

/// <summary>画面へ送る装置の様子（中身の意味は <see cref="DeviceStatus"/>）。</summary>
/// <param name="StockerSlots">すべてのスロットの中身。空いていれば、消耗品は null。</param>
internal sealed record DeviceStatusView(
    string Id,
    int Shortage,
    int CollectWaiting,
    IReadOnlyList<SlotView> MismatchedSlots,
    IReadOnlyList<string> SetupErrors,
    IReadOnlyList<StockerLineView> Stocker,
    SlotSummaryView Slots,
    IReadOnlyList<HeldView> Processing,
    IReadOnlyList<StockerSlotView> StockerSlots,
    IReadOnlyList<HeldView> Handover,
    string Panel)
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
            new SlotSummaryView(status.Slots.Total, status.Slots.Free, status.Slots.Delivering),
            [.. status.Processing.Select(HeldView.From)],
            [.. status.StockerSlots.Select(StockerSlotView.From)],
            [.. status.Handover.Select(HeldView.From)],
            status.TransferPanel.Value);
    }

    public bool Equals(DeviceStatusView? other) =>
        other is not null
        && Id == other.Id
        && Shortage == other.Shortage
        && CollectWaiting == other.CollectWaiting
        && MismatchedSlots.SequenceEqual(other.MismatchedSlots)
        && SetupErrors.SequenceEqual(other.SetupErrors)
        && Stocker.SequenceEqual(other.Stocker)
        && Slots == other.Slots
        && Processing.SequenceEqual(other.Processing)
        && StockerSlots.SequenceEqual(other.StockerSlots)
        && Handover.SequenceEqual(other.Handover)
        && Panel == other.Panel;

    public override int GetHashCode() => HashCode.Combine(Id, Shortage, CollectWaiting, Slots);
}

/// <summary>ストッカーのスロット（ストッカーの番号とスロットの番号。どちらも 1 から）。</summary>
internal sealed record SlotView(int Stocker, int Slot);

internal sealed record StockerLineView(string Variety, int Target, int Have);

internal sealed record SlotSummaryView(int Total, int Free, int Delivering);

/// <summary>スロットと、その中身（空いていれば null）。</summary>
internal sealed record StockerSlotView(int Stocker, int Slot, HeldView? Consumable)
{
    public static StockerSlotView From(StockerSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return new StockerSlotView(
            slot.Slot.Stocker,
            slot.Slot.Slot,
            slot.Content switch
            {
                OccupiedSlot occupied => HeldView.From(occupied.Consumable),
                EmptySlot => null,
                _ => throw new InvalidOperationException($"知らないスロットの中身です（{slot.Content}）。"),
            });
    }
}

/// <summary>
/// 装置の中にある消耗品（中身の意味は <see cref="HeldConsumable"/>）。
/// </summary>
/// <param name="Variety">品種。わからなければ null。</param>
/// <param name="Condition"><c>new</c>（新品）か <c>used</c>（中古品）。品種がわからないものは、記録の状態も確かでないので null。</param>
internal sealed record HeldView(string Id, string? Variety, string? Condition, bool Mismatched, bool DiscardRequested, RecoveryView Recovery)
{
    public static HeldView From(HeldConsumable held)
    {
        ArgumentNullException.ThrowIfNull(held);
        return new HeldView(held.Id.Value, held.Variety?.Value, ConditionText.Of(held.Variety, held.Condition), held.Mismatched, held.DiscardRequested, RecoveryView.From(held.Recovery));
    }
}

/// <summary>回収の様子。</summary>
/// <param name="State"><c>none</c>（回収しない）、<c>needed</c>（回収が要る。まだ頼んでいない）、<c>requested</c>（回収を頼んだ）。</param>
/// <param name="Reason">回収が要るわけ（<c>needed</c> のときだけ）。<c>mismatch</c>、<c>notInSetting</c>、<c>surplus</c>、<c>discard</c>。</param>
internal sealed record RecoveryView(string State, string? Reason)
{
    public static RecoveryView From(RecoveryState recovery) => recovery switch
    {
        NotRecovering => new RecoveryView("none", null),
        RecoveryNeeded needed => new RecoveryView("needed", ReasonText(needed.Reason)),
        RecoveryRequested => new RecoveryView("requested", null),
        _ => throw new InvalidOperationException($"知らない回収の様子です（{recovery}）。"),
    };

    private static string ReasonText(RecoveryReason reason) => reason switch
    {
        RecoveryReason.Mismatch => "mismatch",
        RecoveryReason.NotInSetting => "notInSetting",
        RecoveryReason.Surplus => "surplus",
        RecoveryReason.Discard => "discard",
        _ => throw new InvalidOperationException($"知らない回収のわけです（{reason}）。"),
    };
}

/// <summary>画面へ送るステーションの様子（中身の意味は <see cref="StationStatus"/>）。</summary>
internal sealed record StationStatusView(string Id, int NeedsCheck, int ReplenishWaiting, IReadOnlyList<StationLineView> Stock, IReadOnlyList<StationItemView> Items, string Panel)
{
    public static StationStatusView From(StationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new StationStatusView(
            status.Station.Value,
            status.NeedsCheck,
            status.ReplenishWaiting,
            [.. status.Stock.Select(l => new StationLineView(l.Variety.Value, l.Available, l.Reserved, l.ReplenishWaiting))],
            [.. status.Items.Select(StationItemView.From)],
            status.TransferPanel.Value);
    }

    public bool Equals(StationStatusView? other) =>
        other is not null
        && Id == other.Id
        && NeedsCheck == other.NeedsCheck
        && ReplenishWaiting == other.ReplenishWaiting
        && Stock.SequenceEqual(other.Stock)
        && Items.SequenceEqual(other.Items)
        && Panel == other.Panel;

    public override int GetHashCode() => HashCode.Combine(Id, NeedsCheck, ReplenishWaiting);
}

internal sealed record StationLineView(string Variety, int Available, int Reserved, int ReplenishWaiting);

/// <summary>ステーションにある消耗品。</summary>
/// <param name="Condition"><c>new</c> か <c>used</c>。品種がわからないものは null。</param>
internal sealed record StationItemView(string Id, string? Variety, string? Condition, DateTimeOffset ArrivedAt, StationItemStateView State)
{
    public static StationItemView From(StationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new StationItemView(item.Id.Value, item.Variety?.Value, ConditionText.Of(item.Variety, item.Condition), item.ArrivedAt, StationItemStateView.From(item.State));
    }
}

/// <summary>
/// ステーションにある消耗品の状態。
/// </summary>
/// <param name="Kind"><c>deliverable</c>、<c>reserved</c>、<c>designated</c>、<c>needsCheck</c>、<c>shipping</c>、<c>notDeliverable</c>。</param>
/// <param name="Device">届ける予約と指名の、届け先の装置。</param>
/// <param name="Reason">要確認のわけ（<c>unregisteredId</c>、<c>foundAfterLost</c>、<c>idMismatch</c>）。</param>
internal sealed record StationItemStateView(string Kind, string? Device, string? Reason)
{
    public static StationItemStateView From(StationItemState state) => state switch
    {
        Deliverable => new StationItemStateView("deliverable", null, null),
        ReservedForDelivery reserved => new StationItemStateView("reserved", reserved.Device.Value, null),
        DesignatedFor designated => new StationItemStateView("designated", designated.Device.Value, null),
        CheckNeeded check => new StationItemStateView("needsCheck", null, CheckReasonText.Of(check.Reason)),
        ShippingRequested => new StationItemStateView("shipping", null, null),
        NotDeliverable => new StationItemStateView("notDeliverable", null, null),
        _ => throw new InvalidOperationException($"知らない消耗品の状態です（{state}）。"),
    };
}

/// <summary>新品か中古品かを、画面へ送る文字にする。</summary>
internal static class ConditionText
{
    /// <summary>品種がわからないものは、記録の状態も確かでないので null。</summary>
    public static string? Of(VarietyId? variety, ConsumableCondition condition) =>
        variety is null ? null : Of(condition);

    public static string Of(ConsumableCondition condition) => condition switch
    {
        ConsumableCondition.New => "new",
        ConsumableCondition.Used => "used",
        _ => throw new InvalidOperationException($"知らない消耗品の状態です（{condition}）。"),
    };

    /// <summary>画面から届いた文字を、新品か中古品かにする。知らない文字なら、頼みの形の誤り。</summary>
    public static ConsumableCondition Parse(string? text) => text switch
    {
        "new" => ConsumableCondition.New,
        "used" => ConsumableCondition.Used,
        _ => throw new ArgumentException($"新品か中古品かの指定が正しくありません（{text}）。", nameof(text)),
    };
}
