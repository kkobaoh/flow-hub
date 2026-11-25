namespace FlowHub.Consumables;

/// <summary>
/// 消耗品の構成（加工の種類、品種、入庫の予定）を加える・変える・消した結果。
/// </summary>
public enum ConfigurationChangeResult
{
    /// <summary>保存した（消したときも、これを返す）。</summary>
    Saved,

    /// <summary>同じ名前（ID）のものが、すでにある。入庫の予定では、ほかの品種に対応させてあるか、消耗品の記録にある ID。</summary>
    AlreadyRegistered,

    /// <summary>変える・消す相手か、指した加工の種類や品種がない。</summary>
    NotFound,

    /// <summary>使われているので、変えられない・消せない（品種のある加工の種類、消耗品の記録かストッカーの設定（1個以上）にある品種）。</summary>
    InUse,
}

/// <summary>
/// 画面に出す、消耗品の構成（加工の種類と品種）。
/// </summary>
/// <param name="ProcessTypes">加工の種類（ID の順）。</param>
/// <param name="Varieties">品種（ID の順）。</param>
public sealed record ConsumablesConfigurationView(IReadOnlyList<ProcessTypeSettings> ProcessTypes, IReadOnlyList<VarietyConfigurationView> Varieties)
{
    public bool Equals(ConsumablesConfigurationView? other) =>
        other is not null && ProcessTypes.SequenceEqual(other.ProcessTypes) && Varieties.SequenceEqual(other.Varieties);

    public override int GetHashCode() => HashCode.Combine(ProcessTypes.Count, Varieties.Count);
}

/// <summary>
/// 画面に出す、品種の構成。
/// </summary>
/// <param name="ExpectedIds">入庫の予定（前もって対応させた ID。ID の順）。外せる。</param>
/// <param name="StockedIds">消耗品の記録にある、この品種の ID（ID の順）。外せない。</param>
/// <param name="InUse">使われているか（消耗品の記録か、ストッカーの設定（1個以上）にある）。使われていたら、加工の種類を変えられず、消せない。</param>
public sealed record VarietyConfigurationView(
    VarietyId Variety,
    ProcessTypeId ProcessType,
    IReadOnlyList<ConsumableId> ExpectedIds,
    IReadOnlyList<ConsumableId> StockedIds,
    bool InUse)
{
    public bool Equals(VarietyConfigurationView? other) =>
        other is not null
        && Variety == other.Variety
        && ProcessType == other.ProcessType
        && ExpectedIds.SequenceEqual(other.ExpectedIds)
        && StockedIds.SequenceEqual(other.StockedIds)
        && InUse == other.InUse;

    public override int GetHashCode() => HashCode.Combine(Variety, ProcessType, InUse);
}
