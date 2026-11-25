using FlowHub.Consumables;

namespace FlowHub.ToolPlanning;

/// <summary>消耗品の搬送の目的。</summary>
public enum TransportPurpose
{
    /// <summary>ステーションから装置へ届ける。</summary>
    Delivery,

    /// <summary>装置からステーションへ回収する。</summary>
    Recovery,

    /// <summary>取りやめて積んだまま止まっている AGV から、装置へ届ける。</summary>
    DeliveryFromAgv,

    /// <summary>取りやめて積んだまま止まっている AGV から、ステーションへ返す。</summary>
    ReturnFromAgv,
}

/// <summary>運ぶものの、品種ごとの数。</summary>
/// <param name="Variety">品種。わからないもの（未登録の ID など）は null。</param>
public sealed record CargoCount(VarietyId? Variety, int Count);

/// <summary>
/// 頼んだ消耗品の搬送の中身（画面の搬送の一覧に出すため。配車は運ぶものを知らないので、頼んだ側が出す）。
/// </summary>
/// <param name="Cargo">品種ごとの数（品種の ID の順。わからないものは最後）。</param>
/// <param name="CargoIds">運ぶ消耗品の ID（頼んだ順）。消耗品から、それが入っている搬送を引くために使う。</param>
public sealed record TransportDescription(TransportPurpose Purpose, IReadOnlyList<CargoCount> Cargo, IReadOnlyList<ConsumableId> CargoIds)
{
    public bool Equals(TransportDescription? other) =>
        other is not null && Purpose == other.Purpose && Cargo.SequenceEqual(other.Cargo) && CargoIds.SequenceEqual(other.CargoIds);

    public override int GetHashCode() => HashCode.Combine(Purpose, Cargo.Count);
}
