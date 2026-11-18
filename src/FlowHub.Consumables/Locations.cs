using FlowHub.Dispatching;
using FlowHub.Map;

namespace FlowHub.Consumables;

/// <summary>
/// ストッカーのスロットの場所。番号は、ストッカーの番号の順、同じストッカーの中ではスロットの番号の順に比べる（どちらも 1 から）。
/// </summary>
public readonly record struct SlotRef(int Stocker, int Slot) : IComparable<SlotRef>
{
    public int CompareTo(SlotRef other) =>
        Stocker != other.Stocker ? Stocker.CompareTo(other.Stocker) : Slot.CompareTo(other.Slot);

    public static bool operator <(SlotRef left, SlotRef right) => left.CompareTo(right) < 0;

    public static bool operator >(SlotRef left, SlotRef right) => left.CompareTo(right) > 0;

    public static bool operator <=(SlotRef left, SlotRef right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SlotRef left, SlotRef right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// 消耗品の所在。
/// </summary>
public abstract record ConsumableLocation;

/// <summary>ステーションにある（どのスペースかは持たない）。</summary>
public sealed record AtStation(StationId Station) : ConsumableLocation;

/// <summary>AGV のキャリアに載って搬送されている。</summary>
public sealed record OnAgv(TransportId Transport) : ConsumableLocation;

/// <summary>装置の受け渡し部にある。入れ先のスロットが決まっていれば、それを持つ。</summary>
public sealed record AtTransferUnit(DeviceId Device, SlotRef? PlannedSlot) : ConsumableLocation;

/// <summary>装置のストッカーのスロットにある。</summary>
public sealed record InStocker(DeviceId Device, SlotRef Slot) : ConsumableLocation;

/// <summary>装置の加工部にある（管理しない。存在を描画するだけ）。</summary>
/// <param name="HomeSlot">取り込む前にあったスロット（使い終わったら原則ここに戻る）。わからなければ null。</param>
public sealed record InProcessing(DeviceId Device, SlotRef? HomeSlot) : ConsumableLocation;

/// <summary>
/// 所在不明（ID の照合エラーなどで、人が確かめるか、ステーションで ID が読み取られるまで）。
/// 現物が装置のスロットにあるとわかっているときは、その装置とスロットを持つ（そのスロットはふさがっている）。
/// </summary>
public sealed record UnknownLocation(DeviceId? Device = null, SlotRef? Slot = null) : ConsumableLocation;

/// <summary>
/// 装置の構成。
/// </summary>
public sealed record DeviceLayout
{
    /// <param name="Id">装置の ID。</param>
    /// <param name="TransferPanel">装置の受け渡し部の、受け渡しパネル。</param>
    /// <param name="SlotsPerStocker">ストッカーごとのスロットの数（ストッカーの番号の順。1以上）。</param>
    public DeviceLayout(DeviceId Id, PanelId TransferPanel, IReadOnlyList<int> SlotsPerStocker)
    {
        ArgumentNullException.ThrowIfNull(SlotsPerStocker);
        foreach (var slots in SlotsPerStocker)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots, nameof(SlotsPerStocker));
        }

        this.Id = Id;
        this.TransferPanel = TransferPanel;
        this.SlotsPerStocker = SlotsPerStocker;
    }

    public DeviceId Id { get; }

    public PanelId TransferPanel { get; }

    public IReadOnlyList<int> SlotsPerStocker { get; }

    /// <summary>すべてのスロット（番号の順）。</summary>
    public IEnumerable<SlotRef> AllSlots =>
        SlotsPerStocker.SelectMany((count, index) => Enumerable.Range(1, count).Select(slot => new SlotRef(index + 1, slot)));
}

/// <summary>
/// ステーションの構成。
/// </summary>
public sealed record StationLayout(StationId Id, PanelId TransferPanel);

/// <summary>
/// 加工の種類ごとの設定。
/// </summary>
public sealed record ProcessTypeSettings
{
    /// <param name="Id">加工の種類の ID。</param>
    /// <param name="Carrier">その加工の種類の消耗品を運ぶキャリアの種類。</param>
    /// <param name="CarrierCapacity">キャリアに入る消耗品の個数（1以上）。</param>
    public ProcessTypeSettings(ProcessTypeId Id, CarrierTypeId Carrier, int CarrierCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CarrierCapacity);
        this.Id = Id;
        this.Carrier = Carrier;
        this.CarrierCapacity = CarrierCapacity;
    }

    public ProcessTypeId Id { get; }

    public CarrierTypeId Carrier { get; }

    public int CarrierCapacity { get; }
}
