namespace FlowHub.Consumables.Domain;

/// <summary>
/// 装置のストッカーにある消耗品。
/// </summary>
internal sealed record StockedItem(ConsumableId Id, VarietyId Variety, ConsumableCondition Condition);

/// <summary>
/// 装置のストッカーの設定（装置全体で、品種ごとに何個ストックしておくか）。
/// </summary>
internal sealed record StockerSetting
{
    /// <param name="Counts">品種ごとにストックしておく個数。0以上。</param>
    /// <param name="Capacity">装置全体で置ける数（スロットの合計）。わからないとき（スロットのない装置など）は null。</param>
    public StockerSetting(IReadOnlyDictionary<VarietyId, int> Counts, int? Capacity)
    {
        ArgumentNullException.ThrowIfNull(Counts);
        foreach (var count in Counts.Values)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(Counts));
        }

        if (Capacity is { } capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity, nameof(Capacity));
        }

        this.Counts = Counts;
        this.Capacity = Capacity;
    }

    public IReadOnlyDictionary<VarietyId, int> Counts { get; }

    public int? Capacity { get; }
}

/// <summary>
/// 判断に使う、装置の今の中身。
/// </summary>
/// <param name="Items">ストッカーにある消耗品（装置全体）。</param>
/// <param name="Inbound">その装置へ届ける途中の新品の数（品種ごと）。</param>
/// <param name="RecoveryRequested">すでに回収を頼んだ消耗品。</param>
internal sealed record StockerContents(
    IReadOnlyList<StockedItem> Items,
    IReadOnlyDictionary<VarietyId, int> Inbound,
    IReadOnlySet<ConsumableId> RecoveryRequested);

/// <summary>
/// 回収する理由。
/// </summary>
internal enum RecoveryReason
{
    /// <summary>設定にない品種の新品。</summary>
    NotInSetting,

    /// <summary>設定より多い品種のせいで、ほかの品種の不足を補う空きが足りない。</summary>
    Surplus,
}

/// <summary>
/// 回収が必要な消耗品。
/// </summary>
internal sealed record RecoveryNeed(ConsumableId Id, RecoveryReason Reason);

/// <summary>
/// 不足と回収の判断の結果。
/// </summary>
/// <param name="Shortages">不足している品種と個数（不足している品種だけ）。</param>
/// <param name="Recoveries">新たに回収が必要な消耗品。</param>
internal sealed record StockerEvaluation(IReadOnlyDictionary<VarietyId, int> Shortages, IReadOnlyList<RecoveryNeed> Recoveries);

/// <summary>
/// 装置のストッカーの設定と中身を比べ、不足と回収を判断する。
/// </summary>
internal static class StockerEvaluator
{
    public static StockerEvaluation Evaluate(StockerSetting setting, StockerContents contents)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(contents);

        // 回収を頼んだものは、もうすぐなくなるので数えない
        var remaining = contents.Items.Where(item => !contents.RecoveryRequested.Contains(item.Id)).ToArray();

        var shortages = FindShortages(setting, remaining, contents.Inbound);
        var notInSetting = remaining
            .Where(item => item.Condition == ConsumableCondition.New && !setting.Counts.ContainsKey(item.Variety))
            .Select(item => new RecoveryNeed(item.Id, RecoveryReason.NotInSetting))
            .ToList();
        var surplus = FindSurplusToRecover(setting, remaining, contents.Inbound, shortages, notInSetting.Count);

        return new StockerEvaluation(shortages, [.. notInSetting, .. surplus]);
    }

    /// <summary>
    /// 不足：ストッカーにある新品と、届ける途中の新品の合計が、設定を下回っている品種。中古品は数えない。
    /// </summary>
    private static Dictionary<VarietyId, int> FindShortages(
        StockerSetting setting,
        StockedItem[] remaining,
        IReadOnlyDictionary<VarietyId, int> inbound)
    {
        var shortages = new Dictionary<VarietyId, int>();
        foreach (var (variety, required) in setting.Counts)
        {
            var available = NewCount(remaining, variety) + inbound.GetValueOrDefault(variety);
            if (available < required)
            {
                shortages[variety] = required - available;
            }
        }

        return shortages;
    }

    /// <summary>
    /// 設定より多い品種のうち、不足を補う空きが足りないぶんだけを回収する。容量がわからなければ回収しない。
    /// </summary>
    private static RecoveryNeed[] FindSurplusToRecover(
        StockerSetting setting,
        StockedItem[] remaining,
        IReadOnlyDictionary<VarietyId, int> inbound,
        Dictionary<VarietyId, int> shortages,
        int notInSettingCount)
    {
        if (setting.Capacity is not { } capacity)
        {
            return [];
        }

        // 空き：容量 −（ストッカーにあるもの − 設定にない品種の回収で空くもの ＋ 届ける途中のもの）。中古品もスロットをふさぐ。
        var occupied = remaining.Length - notInSettingCount + inbound.Values.Sum();
        var free = capacity - occupied;
        var lacking = shortages.Values.Sum() - free;
        if (lacking <= 0)
        {
            return [];
        }

        var candidates = setting.Counts
            .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
            .SelectMany(pair =>
            {
                var (variety, required) = pair;
                var inStocker = remaining
                    .Where(item => item.Variety == variety && item.Condition == ConsumableCondition.New)
                    .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                var excess = inStocker.Length + inbound.GetValueOrDefault(variety) - required;
                return inStocker.Take(Math.Max(0, excess));
            });

        return candidates
            .Take(lacking)
            .Select(item => new RecoveryNeed(item.Id, RecoveryReason.Surplus))
            .ToArray();
    }

    private static int NewCount(StockedItem[] items, VarietyId variety) =>
        items.Count(item => item.Variety == variety && item.Condition == ConsumableCondition.New);
}
