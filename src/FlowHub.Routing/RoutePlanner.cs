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
/// 移動コスト ＝ 前進の距離 ÷ 前進速度 ＋ 後退の距離 ÷ 後退速度 ＋ 回転の角度（近い回り）÷ 回転速度
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

        var costs = new Dictionary<Pose, double> { [request.Start] = 0 };
        var previous = new Dictionary<Pose, (Pose From, RouteStep Step)>();
        var queue = new PriorityQueue<Pose, double>();
        queue.Enqueue(request.Start, 0);

        while (queue.TryDequeue(out var current, out var cost))
        {
            if (cost > costs[current])
            {
                continue; // より安い経路で、すでに処理済み
            }

            if (IsGoal(current, request))
            {
                return RouteResult.Success(Reconstruct(current, previous), TimeSpan.FromSeconds(cost));
            }

            foreach (var (step, stepCost) in NextSteps(current, request))
            {
                var next = step.After;
                var nextCost = cost + stepCost;
                if (costs.TryGetValue(next, out var known) && known <= nextCost)
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
