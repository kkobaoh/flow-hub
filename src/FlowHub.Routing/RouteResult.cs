namespace FlowHub.Routing;

/// <summary>
/// 進む向き。
/// </summary>
public enum MoveDirection
{
    Forward,
    Backward,
}

/// <summary>
/// 経路の1つの動作。
/// </summary>
/// <param name="After">動作を終えたあとの姿勢。</param>
public abstract record RouteStep(Pose After);

/// <summary>
/// 隣のノードへの移動。前進でも後退でも、AGV の向きは変わらない。
/// </summary>
public sealed record MoveStep(MoveDirection Direction, string From, string To, int DistanceMm, Pose After) : RouteStep(After);

/// <summary>
/// その場での回転。回転後の絶対角度を指示する（どちら回りかは AGV が判断する）。
/// </summary>
public sealed record RotateStep(string NodeId, int ToHeading, Pose After) : RouteStep(After);

/// <summary>
/// 経路が見つからなかった理由。
/// </summary>
public enum RouteFailure
{
    /// <summary>見つかった（失敗していない）。</summary>
    None,

    /// <summary>出発かゴールのノードがグラフにない。</summary>
    UnknownNode,

    /// <summary>ゴールへたどり着ける経路がない。</summary>
    NoRoute,
}

/// <summary>
/// 経路探索の結果。
/// </summary>
public sealed record RouteResult
{
    private RouteResult(IReadOnlyList<RouteStep> steps, TimeSpan cost, RouteFailure failure)
    {
        Steps = steps;
        Cost = cost;
        Failure = failure;
    }

    public bool Found => Failure == RouteFailure.None;

    /// <summary>動作の並び。見つからなかったときは空。</summary>
    public IReadOnlyList<RouteStep> Steps { get; }

    /// <summary>移動コスト（所要時間）。見つからなかったときは 0。</summary>
    public TimeSpan Cost { get; }

    public RouteFailure Failure { get; }

    internal static RouteResult Success(IReadOnlyList<RouteStep> steps, TimeSpan cost) => new(steps, cost, RouteFailure.None);

    internal static RouteResult Fail(RouteFailure failure) => new([], TimeSpan.Zero, failure);
}
