namespace FlowHub.PathPlanning;

/// <summary>
/// 計画のためのノード（マップのパネル）。
/// </summary>
/// <param name="CanRotate">その場で回転できるか（行き止まりのパネルでは回転できない）。</param>
/// <param name="ForwardEntryOnly">前進でしか入れないか（行き止まりのパネル）。</param>
/// <param name="BackwardExitOnly">後退でしか出られないか（行き止まりのパネル）。</param>
public sealed record PlanningNode(string Id, bool CanRotate, bool ForwardEntryOnly, bool BackwardExitOnly);

/// <summary>
/// 計画のためのつながり（隣り合うパネルの間のライン）。
/// </summary>
public sealed record PlanningEdge
{
    /// <param name="DistanceMm">十字と十字の間の距離（1以上）。</param>
    /// <param name="Heading">From から To へ進むときの絶対角度（0〜359）。</param>
    /// <param name="OneWay">From から To への一方通行か。</param>
    public PlanningEdge(string From, string To, int DistanceMm, int Heading, bool OneWay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DistanceMm);
        Degrees.ThrowIfOutOfRange(Heading);

        this.From = From;
        this.To = To;
        this.DistanceMm = DistanceMm;
        this.Heading = Heading;
        this.OneWay = OneWay;
    }

    public string From { get; }

    public string To { get; }

    public int DistanceMm { get; }

    public int Heading { get; }

    public bool OneWay { get; }
}

/// <summary>
/// 計画のためのマップ（ノードとつながり）。ルーティングには依存せず、自分専用の形で持つ。
/// </summary>
public sealed class PlanningMap
{
    private readonly HashSet<string> _ids;

    public PlanningMap(IEnumerable<PlanningNode> nodes, IEnumerable<PlanningEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        Nodes = nodes.ToArray();
        Edges = edges.ToArray();
        _ids = [];
        foreach (var node in Nodes)
        {
            if (!_ids.Add(node.Id))
            {
                throw new ArgumentException($"ノード {node.Id} が重複しています。", nameof(nodes));
            }
        }

        foreach (var edge in Edges)
        {
            if (!_ids.Contains(edge.From) || !_ids.Contains(edge.To))
            {
                throw new ArgumentException($"つながり {edge.From}-{edge.To} が、ないノードを指しています。", nameof(edges));
            }
        }
    }

    public IReadOnlyList<PlanningNode> Nodes { get; }

    public IReadOnlyList<PlanningEdge> Edges { get; }

    public bool Contains(string nodeId) => _ids.Contains(nodeId);
}

/// <summary>
/// AGV の姿勢（いるノードと、向いている絶対角度）。
/// </summary>
public sealed record AgentPose
{
    public AgentPose(string NodeId, int Heading)
    {
        Degrees.ThrowIfOutOfRange(Heading);
        this.NodeId = NodeId;
        this.Heading = Heading;
    }

    public string NodeId { get; }

    public int Heading { get; }
}

/// <summary>
/// AGV の動きの速さ（所要時間の計算に使う）。
/// </summary>
public sealed record AgentMotion
{
    public AgentMotion(double ForwardSpeedMmPerSecond, double BackwardSpeedMmPerSecond, double RotationSpeedDegreesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ForwardSpeedMmPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BackwardSpeedMmPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RotationSpeedDegreesPerSecond);

        this.ForwardSpeedMmPerSecond = ForwardSpeedMmPerSecond;
        this.BackwardSpeedMmPerSecond = BackwardSpeedMmPerSecond;
        this.RotationSpeedDegreesPerSecond = RotationSpeedDegreesPerSecond;
    }

    public double ForwardSpeedMmPerSecond { get; }

    public double BackwardSpeedMmPerSecond { get; }

    public double RotationSpeedDegreesPerSecond { get; }
}

/// <summary>
/// AGV の行き先。
/// </summary>
public sealed record AgentGoal
{
    /// <param name="Heading">着いたときの向き。指定しない（どの向きでもよい）なら null。</param>
    public AgentGoal(string NodeId, int? Heading)
    {
        if (Heading is { } heading)
        {
            Degrees.ThrowIfOutOfRange(heading);
        }

        this.NodeId = NodeId;
        this.Heading = Heading;
    }

    public string NodeId { get; }

    public int? Heading { get; }
}

/// <summary>
/// 計画に入れる AGV。
/// </summary>
/// <param name="Id">AGV の ID。</param>
/// <param name="Pose">今の姿勢（渡した動作をすべて終えたあとではなく、今いるところ）。</param>
/// <param name="Goal">行き先。搬送も移動もなければ null。</param>
/// <param name="Committed">すでに AGV に渡した動作のうち、まだ終えていないもの（取り消せないので、計画で動かさない）。</param>
/// <param name="Movable">行き先がないときに、通り道を塞いでいたら待機パネルへ動かしてよいか（動かせる状況か）。</param>
public sealed record PlanningAgent(
    string Id,
    AgentPose Pose,
    AgentMotion Motion,
    AgentGoal? Goal,
    IReadOnlyList<AgentAction> Committed,
    bool Movable);

/// <summary>
/// 計画で動かせない AGV（止まっている、ロック中、マニュアル中など）が塞いでいるノード。
/// </summary>
public sealed record PlanningObstacle
{
    /// <param name="OccupiedNodes">塞いでいるノード（パネルとパネルの間で止まっていれば、両側の2つ）。</param>
    public PlanningObstacle(string AgentId, IReadOnlyList<string> OccupiedNodes)
    {
        ArgumentNullException.ThrowIfNull(OccupiedNodes);
        if (OccupiedNodes.Count == 0)
        {
            throw new ArgumentException("障害物は、1つ以上のノードを塞ぎます。", nameof(OccupiedNodes));
        }

        this.AgentId = AgentId;
        this.OccupiedNodes = OccupiedNodes;
    }

    public string AgentId { get; }

    public IReadOnlyList<string> OccupiedNodes { get; }
}

/// <summary>
/// 全台の経路の計画の頼み。
/// </summary>
public sealed record PlanningRequest
{
    /// <param name="Agents">計画に入れる AGV。**並び順が、計画の順番**（先の AGV から経路を決める）。</param>
    /// <param name="Obstacles">計画で動かせない AGV が塞いでいるノード。</param>
    /// <param name="ImprovementBudget">止まらない計画ができたあと、改善にかけてよい時間の上限（0以上）。</param>
    public PlanningRequest(PlanningMap Map, IReadOnlyList<PlanningAgent> Agents, IReadOnlyList<PlanningObstacle> Obstacles, TimeSpan ImprovementBudget)
    {
        ArgumentNullException.ThrowIfNull(Map);
        ArgumentNullException.ThrowIfNull(Agents);
        ArgumentNullException.ThrowIfNull(Obstacles);
        ArgumentOutOfRangeException.ThrowIfLessThan(ImprovementBudget, TimeSpan.Zero);

        var ids = new HashSet<string>();
        foreach (var agent in Agents)
        {
            if (!ids.Add(agent.Id))
            {
                throw new ArgumentException($"AGV {agent.Id} が二度出てきます。", nameof(Agents));
            }

            EnsureNode(Map, agent.Pose.NodeId, nameof(Agents));
            if (agent.Goal is { } goal)
            {
                EnsureNode(Map, goal.NodeId, nameof(Agents));
            }
        }

        foreach (var node in Obstacles.SelectMany(o => o.OccupiedNodes))
        {
            EnsureNode(Map, node, nameof(Obstacles));
        }

        this.Map = Map;
        this.Agents = Agents;
        this.Obstacles = Obstacles;
        this.ImprovementBudget = ImprovementBudget;
    }

    public PlanningMap Map { get; }

    public IReadOnlyList<PlanningAgent> Agents { get; }

    public IReadOnlyList<PlanningObstacle> Obstacles { get; }

    public TimeSpan ImprovementBudget { get; }

    private static void EnsureNode(PlanningMap map, string nodeId, string parameterName)
    {
        if (!map.Contains(nodeId))
        {
            throw new ArgumentException($"ノード {nodeId} はマップにありません。", parameterName);
        }
    }
}

/// <summary>
/// 角度（度）の決まり。
/// </summary>
internal static class Degrees
{
    public const int FullCircle = 360;

    public static void ThrowIfOutOfRange(int degrees)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(degrees);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(degrees, FullCircle);
    }
}
