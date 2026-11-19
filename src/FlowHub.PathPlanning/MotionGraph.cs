namespace FlowHub.PathPlanning;

/// <summary>
/// マップから作る、AGV の動き方の決まりを入れたグラフ（ルーティングと同じ決まり）。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>前進は向いている方向へ、後退はその逆へ進む。動いても向きは変わらない。</item>
/// <item>後退でしか出られないノードからは前進できず、前進でしか入れないノードへは後退で入れない。</item>
/// <item>一方通行は、動く方向で決まる（後退でも逆向きには通れない）。</item>
/// <item>回転の候補は、そのノードのラインの向きと、その逆向き（と、ゴールの向き）。回転できないノードでは回転しない。</item>
/// </list>
/// </remarks>
internal sealed class MotionGraph
{
    private readonly Dictionary<string, PlanningNode> _nodes;
    private readonly Dictionary<string, List<Traversal>> _traversals;
    private readonly Dictionary<string, HashSet<int>> _headings;

    public MotionGraph(PlanningMap map)
    {
        _nodes = map.Nodes.ToDictionary(n => n.Id);
        _traversals = map.Nodes.ToDictionary(n => n.Id, _ => new List<Traversal>());
        _headings = map.Nodes.ToDictionary(n => n.Id, _ => new HashSet<int>());

        foreach (var edge in map.Edges)
        {
            var reverse = Reverse(edge.Heading);
            _traversals[edge.From].Add(new Traversal(edge.To, edge.Heading, edge.DistanceMm));
            if (!edge.OneWay)
            {
                _traversals[edge.To].Add(new Traversal(edge.From, reverse, edge.DistanceMm));
            }

            _headings[edge.From].UnionWith([edge.Heading, reverse]);
            _headings[edge.To].UnionWith([edge.Heading, reverse]);
        }
    }

    public PlanningNode Node(string id) => _nodes[id];

    /// <summary>その場で回転するときに向く候補（ゴールのノードなら、ゴールの向きも入れる）。今の向きは入れない。</summary>
    public IEnumerable<int> RotationTargets(string nodeId, int current, AgentGoal? goal)
    {
        if (!_nodes[nodeId].CanRotate)
        {
            return [];
        }

        var targets = _headings[nodeId].AsEnumerable();
        if (goal is { Heading: { } goalHeading } && goal.NodeId == nodeId)
        {
            targets = targets.Append(goalHeading);
        }

        return targets.Distinct().Where(h => h != current);
    }

    /// <summary>今の姿勢から、隣のノードへ進める動作。</summary>
    public IEnumerable<MoveAction> MovesFrom(string nodeId, int heading)
    {
        var node = _nodes[nodeId];
        foreach (var traversal in _traversals[nodeId])
        {
            if (traversal.Heading == heading && !node.BackwardExitOnly)
            {
                yield return new MoveAction(AgentDirection.Forward, nodeId, traversal.To, traversal.DistanceMm);
            }

            if (traversal.Heading == Reverse(heading) && !_nodes[traversal.To].ForwardEntryOnly)
            {
                yield return new MoveAction(AgentDirection.Backward, nodeId, traversal.To, traversal.DistanceMm);
            }
        }
    }

    /// <summary>
    /// ほかの AGV を気にしなければ、今の姿勢からゴールに着けるか（マップの上に経路があるか）。
    /// </summary>
    public bool CanReach(AgentPose start, AgentGoal goal)
    {
        var visited = new HashSet<(string, int)> { (start.NodeId, start.Heading) };
        var queue = new Queue<(string Node, int Heading)>();
        queue.Enqueue((start.NodeId, start.Heading));
        while (queue.TryDequeue(out var current))
        {
            if (current.Node == goal.NodeId && (goal.Heading is null || goal.Heading == current.Heading))
            {
                return true;
            }

            var next = RotationTargets(current.Node, current.Heading, goal).Select(h => (current.Node, h))
                .Concat(MovesFrom(current.Node, current.Heading).Select(m => (m.To, current.Heading)));
            foreach (var state in next)
            {
                if (visited.Add(state))
                {
                    queue.Enqueue(state);
                }
            }
        }

        return false;
    }

    /// <summary>動作にかかる時間（tick）。回転は近い回りで回る。</summary>
    public static long DurationTicks(AgentAction action, int headingBefore, AgentMotion motion) => action switch
    {
        MoveAction { Direction: AgentDirection.Forward } move => Ticks(move.DistanceMm / motion.ForwardSpeedMmPerSecond),
        MoveAction move => Ticks(move.DistanceMm / motion.BackwardSpeedMmPerSecond),
        RotateAction rotate => Ticks(AngleBetween(headingBefore, rotate.ToHeading) / motion.RotationSpeedDegreesPerSecond),
        _ => throw new ArgumentException($"未知の動作です：{action.GetType().Name}", nameof(action)),
    };

    public static int Reverse(int heading) => (heading + (Degrees.FullCircle / 2)) % Degrees.FullCircle;

    private static int AngleBetween(int from, int to)
    {
        var difference = Math.Abs(from - to) % Degrees.FullCircle;
        return Math.Min(difference, Degrees.FullCircle - difference);
    }

    private static long Ticks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);

    private readonly record struct Traversal(string To, int Heading, int DistanceMm);
}
