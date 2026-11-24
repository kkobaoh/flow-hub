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

    /// <summary>緊急停止中の AGV を、障害物にして計画を立て直すまでの時間（既定は1分）。異常の AGV は、すぐに障害物にする。</summary>
    public TimeSpan EmergencyStopReplanDelay { get; init; } = TimeSpan.FromMinutes(1);
}
