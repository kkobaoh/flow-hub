namespace FlowHub.Routing;

/// <summary>
/// 経路探索。入力を受け取って経路を返すだけで、状態を持たない。
/// </summary>
public interface IRoutePlanner
{
    RouteResult Plan(RouteRequest request);
}

/// <summary>
/// 姿勢（ノードと向き）を状態とし、移動コスト（所要時間）が最小の経路を求める。
/// </summary>
/// <remarks>
/// 移動コスト ＝ 前進の距離 ÷ 前進速度 ＋ 後退の距離 ÷ 後退速度 ＋ 回転の角度（近い回り）÷ 回転速度。
/// 移動コストが同じ経路が複数あるときは、回転の回数が少ないほう、それも同じなら後退の回数が少ないほうを選ぶ。
/// 移動コストは、小数の誤差で「同じ」を見落とさないように、動作ごとに tick（100ナノ秒）の整数にしてから足す。
/// </remarks>
public sealed class RoutePlanner : IRoutePlanner
{
    public RouteResult Plan(RouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var graph = request.Graph;
        if (!graph.Contains(request.Start.NodeId) || !graph.Contains(request.GoalNodeId))
        {
            return RouteResult.Fail(RouteFailure.UnknownNode);
        }

        var costs = new Dictionary<Pose, PathCost> { [request.Start] = PathCost.Zero };
        var previous = new Dictionary<Pose, (Pose From, RouteStep Step)>();
        var queue = new PriorityQueue<Pose, PathCost>();
        queue.Enqueue(request.Start, PathCost.Zero);

        while (queue.TryDequeue(out var current, out var cost))
        {
            if (cost.CompareTo(costs[current]) > 0)
            {
                continue; // より安い経路で、すでに処理済み
            }

            if (IsGoal(current, request))
            {
                return RouteResult.Success(Reconstruct(current, previous), TimeSpan.FromTicks(cost.Ticks));
            }

            foreach (var (step, stepCost) in NextSteps(current, request))
            {
                var next = step.After;
                var nextCost = cost.Add(step, stepCost);
                if (costs.TryGetValue(next, out var known) && known.CompareTo(nextCost) <= 0)
                {
                    continue;
                }

                costs[next] = nextCost;
                previous[next] = (current, step);
                queue.Enqueue(next, nextCost);
            }
        }

        return RouteResult.Fail(RouteFailure.NoRoute);
    }

    private static bool IsGoal(Pose pose, RouteRequest request) =>
        pose.NodeId == request.GoalNodeId
        && (request.GoalHeading is null || pose.Heading == request.GoalHeading);

    /// <summary>
    /// 経路のよさ。移動コスト（tick）、回転の回数、後退の回数の順に比べる（小さいほどよい）。
    /// </summary>
    private readonly record struct PathCost(long Ticks, int Rotations, int Backwards) : IComparable<PathCost>
    {
        public static readonly PathCost Zero = new(0, 0, 0);

        /// <summary>動作を1つ足す。所要時間（秒）は、tick の整数にしてから足す。</summary>
        public PathCost Add(RouteStep step, double seconds) => new(
            Ticks + (long)Math.Round(seconds * TimeSpan.TicksPerSecond),
            Rotations + (step is RotateStep ? 1 : 0),
            Backwards + (step is MoveStep { Direction: MoveDirection.Backward } ? 1 : 0));

        public int CompareTo(PathCost other) =>
            Ticks != other.Ticks ? Ticks.CompareTo(other.Ticks)
            : Rotations != other.Rotations ? Rotations.CompareTo(other.Rotations)
            : Backwards.CompareTo(other.Backwards);
    }

    private static IEnumerable<(RouteStep Step, double Cost)> NextSteps(Pose current, RouteRequest request)
    {
        var graph = request.Graph;
        var motion = request.Motion;
        var node = graph.Node(current.NodeId);

        // その場での回転
        if (node.CanRotate)
        {
            var targets = graph.HeadingsAt(current.NodeId).AsEnumerable();
            if (current.NodeId == request.GoalNodeId && request.GoalHeading is { } goalHeading)
            {
                targets = targets.Append(goalHeading);
            }

            foreach (var heading in targets.Distinct().Where(h => h != current.Heading))
            {
                var angle = Angles.Difference(current.Heading, heading);
                yield return (
                    new RotateStep(current.NodeId, heading, new Pose(current.NodeId, heading)),
                    angle / motion.RotationSpeedDegreesPerSecond);
            }
        }

        // 隣のノードへの移動（向きは変わらない）
        foreach (var traversal in graph.TraversalsFrom(current.NodeId))
        {
            var target = graph.Node(traversal.To);
            var after = new Pose(traversal.To, current.Heading);

            // 前進：向いている方向へ進む。後退でしか出られないノードからは前進できない。
            if (traversal.Heading == current.Heading && !node.BackwardExitOnly)
            {
                yield return (
                    new MoveStep(MoveDirection.Forward, current.NodeId, traversal.To, traversal.DistanceMm, after),
                    traversal.DistanceMm / motion.ForwardSpeedMmPerSecond);
            }

            // 後退：向いている方向の逆へ進む。前進でしか入れないノードへは後退で入れない。
            if (traversal.Heading == Angles.Reverse(current.Heading) && !target.ForwardEntryOnly)
            {
                yield return (
                    new MoveStep(MoveDirection.Backward, current.NodeId, traversal.To, traversal.DistanceMm, after),
                    traversal.DistanceMm / motion.BackwardSpeedMmPerSecond);
            }
        }
    }

    private static List<RouteStep> Reconstruct(Pose goal, Dictionary<Pose, (Pose From, RouteStep Step)> previous)
    {
        var steps = new List<RouteStep>();
        var current = goal;
        while (previous.TryGetValue(current, out var link))
        {
            steps.Add(link.Step);
            current = link.From;
        }

        steps.Reverse();
        return steps;
    }
}
