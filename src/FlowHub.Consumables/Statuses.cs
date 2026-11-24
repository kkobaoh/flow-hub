namespace FlowHub.Consumables;

/// <summary>画面に出す、装置とステーションの様子（気をつけること、ストッカー、在庫）。装置とステーションは、それぞれ ID の順。</summary>
public sealed record ConsumablesStatuses(IReadOnlyList<DeviceStatus> Devices, IReadOnlyList<StationStatus> Stations);

/// <summary>
/// 装置の様子。
/// </summary>
/// <param name="Shortage">不足：届ける途中のものを引いた、まだ手当てのない数（品種の合計）。</param>
/// <param name="CollectWaiting">回収待ち：回収が要ると判断してから、装置から取り出すまでの数（搬送を頼む前のものも、頼んだものも数える）。</param>
/// <param name="MismatchedSlots">ID の食い違い：照合のエラーが来てから、片付くまでのスロット（番号の順）。</param>
/// <param name="SetupErrors">段取り替えのエラー：頼まれたがストッカーの設定になかった品種（ID の順）。設定を変えるか、次のリクエストが通ったら消える。</param>
/// <param name="Stocker">品種ごとの、設定の個数とある新品（設定にある品種と、設定にないがある品種。品種の ID の順）。</param>
public sealed record DeviceStatus(
    DeviceId Device,
    int Shortage,
    int CollectWaiting,
    IReadOnlyList<SlotRef> MismatchedSlots,
    IReadOnlyList<VarietyId> SetupErrors,
    IReadOnlyList<StockerLine> Stocker,
    SlotSummary Slots)
{
    public bool Equals(DeviceStatus? other) =>
        other is not null
        && Device == other.Device
        && Shortage == other.Shortage
        && CollectWaiting == other.CollectWaiting
        && MismatchedSlots.SequenceEqual(other.MismatchedSlots)
        && SetupErrors.SequenceEqual(other.SetupErrors)
        && Stocker.SequenceEqual(other.Stocker)
        && Slots == other.Slots;

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
public sealed record StationStatus(StationId Station, int NeedsCheck, int ReplenishWaiting, IReadOnlyList<StationLine> Stock)
{
    public bool Equals(StationStatus? other) =>
        other is not null && Station == other.Station && NeedsCheck == other.NeedsCheck && ReplenishWaiting == other.ReplenishWaiting && Stock.SequenceEqual(other.Stock);

    public override int GetHashCode() => HashCode.Combine(Station, NeedsCheck, ReplenishWaiting);
}

/// <summary>ステーションの、品種ごとの数。</summary>
/// <param name="Available">届けられる新品（要確認でなく、届ける予約も指名もないもの）。</param>
/// <param name="ReplenishWaiting">補充を頼んで、まだ入っていない数。</param>
public sealed record StationLine(VarietyId Variety, int Available, int ReplenishWaiting);
