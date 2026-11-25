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

    /// <summary>
    /// 使われているので、変えられない・消せない（品種のある加工の種類、消耗品の記録かストッカーの設定（1個以上）にある品種、
    /// 消耗品・予約・指名・受け渡しの途中がある装置とステーション）。
    /// </summary>
    InUse,

    /// <summary>受け渡しパネルが、公開中のマップの受け渡しパネルでないか、ほかの装置かステーションが使っている。</summary>
    PanelUnavailable,

    /// <summary>減らすストッカーかスロットが空でない（消耗品がある、届ける入れ先に決めた、加工中の消耗品の元のスロット）。</summary>
    SlotsOccupied,

    /// <summary>減らしたあとのスロットの数より、ストッカーの設定の個数の合計が多い。</summary>
    ExceedsStockerSetting,

    /// <summary>ステーションは1台だけ（ステーションの選び方のルールが決まるまで）。</summary>
    StationLimit,
}

/// <summary>
/// 画面に出す、消耗品の構成（加工の種類、品種、装置、ステーション）。
/// </summary>
/// <param name="ProcessTypes">加工の種類（ID の順）。</param>
/// <param name="Varieties">品種（ID の順）。</param>
/// <param name="Devices">装置（ID の順）。</param>
/// <param name="Stations">ステーション（ID の順）。</param>
public sealed record ConsumablesConfigurationView(
    IReadOnlyList<ProcessTypeSettings> ProcessTypes,
    IReadOnlyList<VarietyConfigurationView> Varieties,
    IReadOnlyList<DeviceConfigurationView> Devices,
    IReadOnlyList<StationConfigurationView> Stations)
{
    public bool Equals(ConsumablesConfigurationView? other) =>
        other is not null
        && ProcessTypes.SequenceEqual(other.ProcessTypes)
        && Varieties.SequenceEqual(other.Varieties)
        && Devices.SequenceEqual(other.Devices)
        && Stations.SequenceEqual(other.Stations);

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

/// <summary>
/// 画面に出す、装置の構成。
/// </summary>
/// <param name="SlotsPerStocker">ストッカーごとのスロットの数。</param>
/// <param name="OccupiedSlots">ふさがっているスロット（減らせない。番号の順）。</param>
/// <param name="InUse">使われているか（消耗品・届ける予約・指名・受け渡しの途中がある）。使われていたら、受け渡しパネルを変えられず、消せない。</param>
public sealed record DeviceConfigurationView(
    DeviceId Device,
    Map.PanelId TransferPanel,
    IReadOnlyList<int> SlotsPerStocker,
    IReadOnlyList<SlotRef> OccupiedSlots,
    bool InUse)
{
    public bool Equals(DeviceConfigurationView? other) =>
        other is not null
        && Device == other.Device
        && TransferPanel == other.TransferPanel
        && SlotsPerStocker.SequenceEqual(other.SlotsPerStocker)
        && OccupiedSlots.SequenceEqual(other.OccupiedSlots)
        && InUse == other.InUse;

    public override int GetHashCode() => HashCode.Combine(Device, TransferPanel, InUse);
}

/// <summary>画面に出す、ステーションの構成。</summary>
/// <param name="InUse">使われているか（消耗品・届ける予約・回収の予約・受け渡しの途中がある）。使われていたら、受け渡しパネルを変えられず、消せない。</param>
public sealed record StationConfigurationView(StationId Station, Map.PanelId TransferPanel, bool InUse);
