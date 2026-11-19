namespace FlowHub.Traffic;

/// <summary>
/// 全台の経路の計画の設定。
/// </summary>
public sealed record TrafficPlanningOptions
{
    /// <summary>計画する時間の長さの上限（既定は10分）。目安は、マップの端から端まで走る時間の、およそ5倍。</summary>
    public TimeSpan Horizon { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>止まらない計画ができたあと、改善にかけてよい時間の上限（既定は200ミリ秒）。</summary>
    public TimeSpan ImprovementBudget { get; init; } = TimeSpan.FromMilliseconds(200);
}
