namespace FlowHub.PathPlanning;

/// <summary>
/// 進む向き。
/// </summary>
public enum AgentDirection
{
    Forward,
    Backward,
}

/// <summary>
/// AGV の1つの動作。
/// </summary>
public abstract record AgentAction;

/// <summary>隣のノードへ前進または後退する。</summary>
public sealed record MoveAction(AgentDirection Direction, string From, string To, int DistanceMm) : AgentAction;

/// <summary>その場で、絶対角度へ回転する。</summary>
public sealed record RotateAction(string NodeId, int ToHeading) : AgentAction;

/// <summary>
/// 計画の時刻つきの動作。時刻は、計画を立てた時点からの経過時間。
/// </summary>
public sealed record TimedAction(AgentAction Action, TimeSpan Start, TimeSpan End);

/// <summary>
/// AGV ごとの計画の結果の種類。
/// </summary>
public enum PlanStatus
{
    /// <summary>行き先までの計画ができた（または、邪魔なので待機パネルへ動かす計画ができた）。</summary>
    Planned,

    /// <summary>行き先がなく、動かさない。</summary>
    Stays,

    /// <summary>マップの上に、そもそも経路がない（別の島にいる、一方通行で行けないなど）。</summary>
    NoRoute,

    /// <summary>今は、ほかの AGV（障害物）に塞がれていて行けない。計画を立て直すたびに、また試す。</summary>
    Blocked,
}

/// <summary>
/// AGV ごとの計画。
/// </summary>
/// <param name="Actions">時刻つきの動作の並び（すでに渡した動作も含む）。</param>
public sealed record AgentPlan(string AgentId, PlanStatus Status, IReadOnlyList<TimedAction> Actions);

/// <summary>
/// 全台の経路の計画の結果。
/// </summary>
/// <param name="Plans">AGV ごとの計画（頼みの並び順）。</param>
/// <param name="PassingOrder">ノードごとの、通る AGV の順番（交通整理が守らせる）。</param>
public sealed record PlanningResult(IReadOnlyList<AgentPlan> Plans, IReadOnlyDictionary<string, IReadOnlyList<string>> PassingOrder);

/// <summary>
/// 全台の経路を計画する。状態を持たない計算。
/// </summary>
public interface IPathPlanner
{
    PlanningResult Plan(PlanningRequest request);
}
