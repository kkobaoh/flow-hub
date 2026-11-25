using FlowHub.Map;

namespace FlowHub.Consumables;

/// <summary>画面に出す、装置とステーションの様子（気をつけること、ストッカー、在庫）。装置とステーションは、それぞれ ID の順。</summary>
/// <param name="Varieties">登録してある品種（ID の順。ストッカーの設定に加えたり、要確認を直したりするときに選ぶもの）。</param>
public sealed record ConsumablesStatuses(IReadOnlyList<DeviceStatus> Devices, IReadOnlyList<StationStatus> Stations, IReadOnlyList<VarietyId> Varieties);

/// <summary>
/// 装置の様子。
/// </summary>
/// <param name="Shortage">不足：届ける途中のものを引いた、まだ手当てのない数（品種の合計）。</param>
/// <param name="CollectWaiting">回収待ち：回収が要ると判断してから、装置から取り出すまでの数（搬送を頼む前のものも、頼んだものも数える）。</param>
/// <param name="MismatchedSlots">ID の食い違い：照合のエラーが来てから、片付くまでのスロット（番号の順）。</param>
/// <param name="SetupErrors">段取り替えのエラー：頼まれたがストッカーの設定になかった品種（ID の順）。設定を変えるか、次のリクエストが通ったら消える。</param>
/// <param name="Stocker">品種ごとの、設定の個数とある新品（設定にある品種と、設定にないがある品種。品種の ID の順）。</param>
/// <param name="Processing">加工部にある消耗品（ID の順）。</param>
/// <param name="StockerSlots">すべてのスロットの中身（スロットの番号の順）。取り込み中の元のスロットは、空きとして出す。</param>
/// <param name="Handover">受け渡し部にある消耗品（ID の順）。</param>
/// <param name="TransferPanel">装置の受け渡しパネル。</param>
public sealed record DeviceStatus(
    DeviceId Device,
    int Shortage,
    int CollectWaiting,
    IReadOnlyList<SlotRef> MismatchedSlots,
    IReadOnlyList<VarietyId> SetupErrors,
    IReadOnlyList<StockerLine> Stocker,
    SlotSummary Slots,
    IReadOnlyList<HeldConsumable> Processing,
    IReadOnlyList<StockerSlot> StockerSlots,
    IReadOnlyList<HeldConsumable> Handover,
    PanelId TransferPanel)
{
    public bool Equals(DeviceStatus? other) =>
        other is not null
        && Device == other.Device
        && Shortage == other.Shortage
        && CollectWaiting == other.CollectWaiting
        && MismatchedSlots.SequenceEqual(other.MismatchedSlots)
        && SetupErrors.SequenceEqual(other.SetupErrors)
        && Stocker.SequenceEqual(other.Stocker)
        && Slots == other.Slots
        && Processing.SequenceEqual(other.Processing)
        && StockerSlots.SequenceEqual(other.StockerSlots)
        && Handover.SequenceEqual(other.Handover)
        && TransferPanel == other.TransferPanel;

    public override int GetHashCode() => HashCode.Combine(Device, Shortage, CollectWaiting, Slots);
}

/// <summary>ストッカーの設定の1行。</summary>
/// <param name="Target">設定の個数（設定にない品種は 0）。</param>
/// <param name="Have">ある新品（ストッカーにあるものと、届ける途中のもの）。</param>
public sealed record StockerLine(VarietyId Variety, int Target, int Have);

/// <summary>スロットの数。</summary>
/// <param name="Free">空き（届ける途中のものや、ふさがっているスロットを引いた数）。</param>
/// <param name="Delivering">届ける予約をしている数。</param>
public sealed record SlotSummary(int Total, int Free, int Delivering);

/// <summary>
/// ステーションの様子。
/// </summary>
/// <param name="NeedsCheck">要確認の消耗品の数。</param>
/// <param name="ReplenishWaiting">補充を頼んで、まだ入っていない数（すべての品種の合計。どのステーションに入るかは決まっていないので、どのステーションにも同じ数を出す）。</param>
/// <param name="Stock">品種ごとの数（登録してあるすべての品種。ID の順）。</param>
/// <param name="Items">ステーションにある消耗品（ID の順）。</param>
/// <param name="TransferPanel">ステーションの受け渡しパネル。</param>
public sealed record StationStatus(StationId Station, int NeedsCheck, int ReplenishWaiting, IReadOnlyList<StationLine> Stock, IReadOnlyList<StationItem> Items, PanelId TransferPanel)
{
    public bool Equals(StationStatus? other) =>
        other is not null
        && Station == other.Station
        && NeedsCheck == other.NeedsCheck
        && ReplenishWaiting == other.ReplenishWaiting
        && Stock.SequenceEqual(other.Stock)
        && Items.SequenceEqual(other.Items)
        && TransferPanel == other.TransferPanel;

    public override int GetHashCode() => HashCode.Combine(Station, NeedsCheck, ReplenishWaiting);
}

/// <summary>ステーションの、品種ごとの数。</summary>
/// <param name="Available">届けられる新品（要確認でなく、届ける予約も指名もないもの）。</param>
/// <param name="Reserved">このステーションにあって、届ける予約が付いている数。</param>
/// <param name="ReplenishWaiting">補充を頼んで、まだ入っていない数。</param>
public sealed record StationLine(VarietyId Variety, int Available, int Reserved, int ReplenishWaiting);

/// <summary>
/// 装置の中（加工部、ストッカー、受け渡し部）にある消耗品。
/// </summary>
/// <param name="Variety">品種。未登録の ID など、わからなければ null。</param>
/// <param name="Mismatched">照合のエラーや交換で記録と現物が合わず、現物を確かめるまでスロットをふさいでいる。</param>
/// <param name="DiscardRequested">装置から廃棄リクエストを受けた。</param>
/// <param name="Recovery">回収の様子。</param>
public sealed record HeldConsumable(ConsumableId Id, VarietyId? Variety, ConsumableCondition Condition, bool Mismatched, bool DiscardRequested, RecoveryState Recovery);

/// <summary>装置にある消耗品の、回収の様子。</summary>
public abstract record RecoveryState;

/// <summary>回収しない。</summary>
public sealed record NotRecovering : RecoveryState;

/// <summary>回収が要ると判断した（まだ搬送を頼んでいない）。</summary>
public sealed record RecoveryNeeded(RecoveryReason Reason) : RecoveryState;

/// <summary>回収の搬送を頼んで、まだ取り出していない。</summary>
public sealed record RecoveryRequested : RecoveryState;

/// <summary>ストッカーのスロットと、その中身。</summary>
public sealed record StockerSlot(SlotRef Slot, SlotContent Content);

/// <summary>スロットの中身。</summary>
public abstract record SlotContent;

/// <summary>空いている。</summary>
public sealed record EmptySlot : SlotContent;

/// <summary>消耗品がある。</summary>
public sealed record OccupiedSlot(HeldConsumable Consumable) : SlotContent;

/// <summary>
/// ステーションにある消耗品。
/// </summary>
/// <param name="Variety">品種。未登録の ID など、わからなければ null。</param>
/// <param name="ArrivedAt">ステーションに入った日時。</param>
public sealed record StationItem(ConsumableId Id, VarietyId? Variety, ConsumableCondition Condition, DateTimeOffset ArrivedAt, StationItemState State);

/// <summary>ステーションにある消耗品の状態。いくつも当てはまるときは、要確認、出庫を頼んだ、届ける予約、指名、の順に1つを出す。</summary>
public abstract record StationItemState;

/// <summary>届けられる新品。</summary>
public sealed record Deliverable : StationItemState;

/// <summary>届ける予約が付いている。</summary>
public sealed record ReservedForDelivery(DeviceId Device) : StationItemState;

/// <summary>指名されている。</summary>
public sealed record DesignatedFor(DeviceId Device) : StationItemState;

/// <summary>要確認。</summary>
public sealed record CheckNeeded(NeedsCheckReason Reason) : StationItemState;

/// <summary>出庫を頼んで、まだ出ていない。</summary>
public sealed record ShippingRequested : StationItemState;

/// <summary>届けない中古品。</summary>
public sealed record NotDeliverable : StationItemState;

/// <summary>要確認かどうかと、要確認になったわけ。</summary>
public enum NeedsCheckReason
{
    /// <summary>要確認ではない。</summary>
    NotNeeded,

    /// <summary>登録されていない ID として、ステーションに入ってきた。</summary>
    UnregisteredId,

    /// <summary>所在不明だったものが、ステーションで見つかった。</summary>
    FoundAfterLost,

    /// <summary>装置で ID が食い違った（照合のエラーや交換）ので、回収した。</summary>
    IdMismatch,
}

/// <summary>ストッカーの設定を保存した結果。</summary>
public enum StockerSettingResult
{
    Saved,

    /// <summary>個数の合計が、装置のスロットの数を超えている（保存していない）。</summary>
    ExceedsSlots,
}

/// <summary>出庫を頼んだ結果。</summary>
public enum ShipmentResult
{
    /// <summary>ステーションに出庫を頼んだ。</summary>
    Requested,

    /// <summary>ステーションにない（頼んでいない）。</summary>
    NotAtStation,

    /// <summary>AGV を割り当て済みの、届ける搬送に入っている（頼んでいない）。</summary>
    InAssignedTransport,
}

/// <summary>
/// 1個の消耗品の記録（消耗品の検索の画面に出す）。
/// </summary>
/// <param name="Variety">品種。わからなければ null。</param>
/// <param name="ProcessType">加工の種類。わからなければ null。</param>
/// <param name="Check">要確認かどうかと、要確認になったわけ。</param>
/// <param name="Reservation">届ける・回収するの予約。</param>
/// <param name="DesignatedFor">指名した届け先の装置。指名していなければ null。</param>
/// <param name="ShippingRequested">出庫を頼んで、まだ出ていない。</param>
public sealed record ConsumableRecord(
    ConsumableId Id,
    VarietyId? Variety,
    ProcessTypeId? ProcessType,
    ConsumableCondition Condition,
    ConsumableLocation Location,
    NeedsCheckReason Check,
    ConsumableReservation Reservation,
    DeviceId? DesignatedFor,
    bool ShippingRequested);

/// <summary>消耗品に付いている、搬送の予約。</summary>
public abstract record ConsumableReservation;

/// <summary>予約はない。</summary>
public sealed record NoReservation : ConsumableReservation;

/// <summary>装置へ届ける予約。</summary>
public sealed record DeliveryReservation(DeviceId Device) : ConsumableReservation;

/// <summary>装置から回収する予約。</summary>
public sealed record RecoveryReservation : ConsumableReservation;

/// <summary>届ける消耗品を指名した結果。</summary>
public enum DesignationResult
{
    Designated,

    /// <summary>指名できない（ステーションにある、要確認でない新品で、届ける予約も出庫の頼みも指名もないものに限る。何も変えていない）。</summary>
    NotDesignatable,
}

/// <summary>指名を取り消した結果。</summary>
public enum DesignationCancelResult
{
    Canceled,

    /// <summary>AGV を割り当て済みの、届ける搬送に入っている（取り消していない）。</summary>
    InAssignedTransport,
}

/// <summary>要確認の消耗品を直した結果。</summary>
public enum CorrectionResult
{
    Corrected,

    /// <summary>ステーションにある要確認の消耗品ではない（ほかの人が直した、出庫したなど。何も変えていない）。</summary>
    NotNeedingCheck,
}

/// <summary>在庫の手入力（登録・直す・消す）の結果。断ったときは、何も変えていない。</summary>
public enum ManualEntryResult
{
    Saved,

    /// <summary>その ID は、もう登録してある（登録のとき）。</summary>
    AlreadyRegistered,

    /// <summary>記録にない（直す・消すとき。ほかの人が消した、出庫したなど）。</summary>
    NotFound,

    /// <summary>AGV の中、加工部・受け渡し部にあるもの、出庫を頼んだもの（直す・消すとき）。</summary>
    NotEditable,

    /// <summary>AGV を割り当て済みの搬送の予約が付いている（直す・消すとき。搬送計画が確かめる）。</summary>
    InAssignedTransport,

    /// <summary>選んだスロットが、ほかの消耗品でふさがっている。</summary>
    SlotTaken,
}
