using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Domain;

/// <summary>
/// 充電の設定。値は AGV の種類や構成ごとに変えられる。
/// </summary>
internal sealed record ChargingPolicy
{
    /// <param name="GoChargeBelowPercent">充電残量がこの値を下回ったら、搬送を割り当てず充電に向かわせる（0〜100）。</param>
    /// <param name="ResumeAtPercent">充電に向かった AGV は、この値以上に回復したら割り当てに戻す（0〜100）。充電に向かう値以上（同じ値はよい）。</param>
    public ChargingPolicy(int GoChargeBelowPercent, int ResumeAtPercent)
    {
        ThrowIfNotPercent(GoChargeBelowPercent, nameof(GoChargeBelowPercent));
        ThrowIfNotPercent(ResumeAtPercent, nameof(ResumeAtPercent));
        ArgumentOutOfRangeException.ThrowIfLessThan(ResumeAtPercent, GoChargeBelowPercent, nameof(ResumeAtPercent));
        this.GoChargeBelowPercent = GoChargeBelowPercent;
        this.ResumeAtPercent = ResumeAtPercent;
    }

    public int GoChargeBelowPercent { get; }

    public int ResumeAtPercent { get; }

    /// <summary>
    /// 充電残量の点で、搬送を割り当てられるか。
    /// </summary>
    /// <param name="batteryPercent">今の充電残量。</param>
    /// <param name="recoveringCharge">充電に向かったあと、まだ割り当てに戻していないか。</param>
    public bool AllowsAssignment(int batteryPercent, bool recoveringCharge) =>
        recoveringCharge ? batteryPercent >= ResumeAtPercent : batteryPercent >= GoChargeBelowPercent;

    private static void ThrowIfNotPercent(int value, string paramName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100, paramName);
    }
}

/// <summary>
/// 割り当ての候補の AGV。
/// </summary>
/// <param name="Id">AGV の ID。</param>
/// <param name="Position">今の位置と向き。</param>
/// <param name="IsIdle">待機中か（緊急停止中・異常・位置不明・位置リセット中・搬送中でない）。</param>
/// <param name="BatteryPercent">今の充電残量。</param>
/// <param name="RecoveringCharge">充電に向かったあと、まだ割り当てに戻していないか。</param>
/// <param name="Carrier">AGV のキャリアの種類。</param>
/// <param name="CumulativeDistanceMm">これまでの累計の走行距離。</param>
internal sealed record AgvCandidate(
    AgvId Id,
    Pose Position,
    bool IsIdle,
    int BatteryPercent,
    bool RecoveringCharge,
    CarrierTypeId Carrier,
    long CumulativeDistanceMm);

/// <summary>
/// 割り当ての依頼。
/// </summary>
/// <param name="Source">搬送元の受け渡しパネル。</param>
/// <param name="Carrier">搬送の要求が求めるキャリアの種類。</param>
internal sealed record AssignmentRequest(PanelId Source, CarrierTypeId Carrier);

/// <summary>
/// 搬送の要求に、どの AGV を割り当てるかを決める。
/// </summary>
internal static class AgvAssigner
{
    /// <summary>
    /// 割り当てられる AGV のうち、搬送元に一番近い AGV を選ぶ。
    /// 近さが同じなら累計の走行距離が短い AGV、それも同じなら AGV の ID の順で選ぶ。
    /// </summary>
    /// <param name="costOf">AGV の今の位置から搬送元までの移動コスト。経路が見つからなければ null。</param>
    /// <returns>選んだ AGV。割り当てられる AGV がいなければ null。</returns>
    public static AgvId? Choose(
        AssignmentRequest request,
        IEnumerable<AgvCandidate> candidates,
        ChargingPolicy policy,
        Func<AgvCandidate, TimeSpan?> costOf)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(costOf);

        // 移動コストの計算（経路探索）は重いので、割り当てられる AGV だけで求める
        var reachable = candidates
            .Where(agv => agv.IsIdle
                && agv.Carrier == request.Carrier
                && policy.AllowsAssignment(agv.BatteryPercent, agv.RecoveringCharge))
            .Select(agv => (Agv: agv, Cost: costOf(agv)))
            .Where(pair => pair.Cost is not null);

        return reachable
            .OrderBy(pair => pair.Cost)
            .ThenBy(pair => pair.Agv.CumulativeDistanceMm)
            .ThenBy(pair => pair.Agv.Id.Value, StringComparer.Ordinal)
            .Select(pair => (AgvId?)pair.Agv.Id)
            .FirstOrDefault();
    }
}
