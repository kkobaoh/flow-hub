namespace FlowHub.Routing;

/// <summary>
/// AGV の姿勢（いるノードと、向いている絶対角度）。
/// </summary>
public sealed record Pose
{
    public Pose(string NodeId, int Heading)
    {
        Angles.ThrowIfOutOfRange(Heading);
        this.NodeId = NodeId;
        this.Heading = Heading;
    }

    public string NodeId { get; }

    public int Heading { get; }
}

/// <summary>
/// AGV の動きの速さ。移動コスト（所要時間）の計算に使う。
/// </summary>
public sealed record MotionProfile
{
    public MotionProfile(double ForwardSpeedMmPerSecond, double BackwardSpeedMmPerSecond, double RotationSpeedDegreesPerSecond)
    {
        this.ForwardSpeedMmPerSecond = ForwardSpeedMmPerSecond;
        this.BackwardSpeedMmPerSecond = BackwardSpeedMmPerSecond;
        this.RotationSpeedDegreesPerSecond = RotationSpeedDegreesPerSecond;
    }

    /// <summary>前進の速さ（mm/秒）。正の値。</summary>
    public double ForwardSpeedMmPerSecond { get; init => field = Positive(value); }

    /// <summary>後退の速さ（mm/秒）。正の値。</summary>
    public double BackwardSpeedMmPerSecond { get; init => field = Positive(value); }

    /// <summary>回転の速さ（度/秒）。正の値。</summary>
    public double RotationSpeedDegreesPerSecond { get; init => field = Positive(value); }

    private static double Positive(double value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
        return value;
    }
}

/// <summary>
/// 経路探索の依頼。
/// </summary>
/// <param name="Graph">探索するグラフ。</param>
/// <param name="Start">出発の姿勢。</param>
/// <param name="GoalNodeId">ゴールのノード。</param>
/// <param name="GoalHeading">ゴールで向いていてほしい絶対角度。null なら向きは問わない。</param>
/// <param name="Motion">AGV の動きの速さ。</param>
public sealed record RouteRequest(RouteGraph Graph, Pose Start, string GoalNodeId, int? GoalHeading, MotionProfile Motion);
