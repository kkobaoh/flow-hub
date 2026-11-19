namespace FlowHub.PathPlanning;

/// <summary>
/// 止まらない計画。AGV に順番を付け、先の AGV から順に、時刻つきで経路を決める。あとの AGV は、先の AGV を避ける。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>動かせない AGV（障害物）と、すでに渡した動作（取り消せない）は、一番先に固定する。</item>
/// <item>まだ計画していない AGV の今いるノードは、先の AGV にとって「ずっと塞がれている」とみなす。そのせいで行けなければ、
/// 塞いでいる AGV の順番を繰り上げて、先に計画してから、もう一度試す。</item>
/// <item>行き先に着いた AGV と、行き先のない AGV は、そのノードに止まり続けるものとする。</item>
/// <item>計画できなければ、マップの上に経路がないなら「経路がない」、あるなら「塞がれていて行けない」とし、今いるノードに止まり続けるものとする。</item>
/// </list>
/// </remarks>
public sealed class PrioritizedPlanner : IPathPlanner
{
    public PlanningResult Plan(PlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Run(request).Execute();
    }

    /// <summary>1回の計画。</summary>
    private sealed class Run
    {
        private readonly PlanningRequest _request;
        private readonly MotionGraph _graph;
        private readonly Occupancy _occupancy = new();
        private readonly long _horizon;
        private readonly Dictionary<string, AgentState> _agents = [];
        private readonly HashSet<string> _inProgress = [];

        public Run(PlanningRequest request)
        {
            _request = request;
            _graph = new MotionGraph(request.Map);
            _horizon = request.Horizon.Ticks;
        }

        public PlanningResult Execute()
        {
            foreach (var obstacle in _request.Obstacles)
            {
                foreach (var node in obstacle.OccupiedNodes)
                {
                    _occupancy.Reserve(node, new Reservation(obstacle.AgentId, 0, Occupancy.Forever, Provisional: false));
                }
            }

            // すでに渡した動作を、先に固定する。そのあとの今いるノードは、計画するまで仮に「ずっと塞いでいる」とする
            foreach (var agent in _request.Agents)
            {
                var committed = Schedule(agent.Pose, 0, agent.Committed, agent.Motion);
                var (node, since) = ReserveTimeline(agent.Id, agent.Pose.NodeId, 0, committed);
                var end = committed.Count > 0 ? committed[^1].End : 0;
                var pose = committed.Aggregate(agent.Pose, After);
                _agents[agent.Id] = new AgentState(agent, committed, pose, end, since);
                _occupancy.Reserve(node, new Reservation(agent.Id, since, Occupancy.Forever, Provisional: true));
            }

            foreach (var agent in _request.Agents)
            {
                if (_agents[agent.Id].Status is null)
                {
                    PlanAgent(_agents[agent.Id]);
                }
            }

            var plans = _request.Agents
                .Select(agent => _agents[agent.Id])
                .Select(state => new AgentPlan(
                    state.Agent.Id,
                    state.Status!.Value,
                    state.Committed.Concat(state.Planned).Select(s => new TimedAction(s.Action, TimeSpan.FromTicks(s.Start), TimeSpan.FromTicks(s.End))).ToArray()))
                .ToArray();
            return new PlanningResult(plans, _occupancy.PassingOrder());
        }

        private void PlanAgent(AgentState state)
        {
            var agent = state.Agent;
            _occupancy.RemoveProvisional(agent.Id);
            if (agent.Goal is not { } goal)
            {
                Settle(state, PlanStatus.Stays, []);
                return;
            }

            var steps = Search(state, goal, ignoreProvisional: false);
            if (steps is null && TryPromoteBlockers(state, goal))
            {
                steps = Search(state, goal, ignoreProvisional: false);
            }

            if (steps is not null)
            {
                Settle(state, PlanStatus.Planned, steps);
                return;
            }

            Settle(state, _graph.CanReach(state.Pose, goal) ? PlanStatus.Blocked : PlanStatus.NoRoute, []);
        }

        /// <summary>
        /// まだ計画していない AGV がいなければ行けるなら、その AGV たちの順番を繰り上げて、先に計画する。
        /// 繰り上げる AGV が、今繰り上げている途中の AGV（輪になる）なら、繰り上げない。
        /// </summary>
        private bool TryPromoteBlockers(AgentState state, AgentGoal goal)
        {
            var withoutPending = Search(state, goal, ignoreProvisional: true);
            if (withoutPending is null)
            {
                return false;
            }

            var blockers = withoutPending
                .SelectMany(step => step.Action is MoveAction move ? [move.From, move.To] : Array.Empty<string>())
                .SelectMany(_occupancy.ProvisionalOwnersAt)
                .Where(owner => owner != state.Agent.Id)
                .Distinct()
                .ToArray();
            if (blockers.Length == 0 || blockers.Any(_inProgress.Contains))
            {
                return false;
            }

            // 繰り上げた AGV を計画している間は、この AGV が今いるノードを、仮に塞いでおく
            _inProgress.Add(state.Agent.Id);
            _occupancy.Reserve(state.Pose.NodeId, new Reservation(state.Agent.Id, state.OccupiedSince, Occupancy.Forever, Provisional: true));
            foreach (var blocker in _request.Agents.Select(a => a.Id).Where(blockers.Contains))
            {
                if (_agents[blocker].Status is null)
                {
                    PlanAgent(_agents[blocker]);
                }
            }

            _occupancy.RemoveProvisional(state.Agent.Id);
            _inProgress.Remove(state.Agent.Id);
            return true;
        }

        private IReadOnlyList<TimedStep>? Search(AgentState state, AgentGoal goal, bool ignoreProvisional) =>
            SafeIntervalSearch.Search(_graph, _occupancy, ignoreProvisional, state.Agent.Motion, state.Pose, state.EndTime, goal, _horizon);

        /// <summary>計画を決めて、使うノードの時間帯を予約する。最後のノードには止まり続ける。</summary>
        private void Settle(AgentState state, PlanStatus status, IReadOnlyList<TimedStep> steps)
        {
            var (node, since) = ReserveTimeline(state.Agent.Id, state.Pose.NodeId, state.OccupiedSince, steps);
            _occupancy.Reserve(node, new Reservation(state.Agent.Id, since, Occupancy.Forever, Provisional: false));
            state.Status = status;
            state.Planned = steps;
        }

        /// <summary>
        /// 動作の並びが使うノードの時間帯を予約する。動いている間は、出発したノードと着くノードの両方を使う。
        /// </summary>
        /// <returns>最後にいるノードと、そこを使い始めた時刻。</returns>
        private (string Node, long Since) ReserveTimeline(string owner, string startNode, long since, IReadOnlyList<TimedStep> steps)
        {
            var node = startNode;
            foreach (var step in steps)
            {
                if (step.Action is MoveAction move)
                {
                    _occupancy.Reserve(node, new Reservation(owner, since, step.End, Provisional: false));
                    node = move.To;
                    since = step.Start;
                }
            }

            return (node, since);
        }

        /// <summary>すでに渡した動作に、時刻を付ける（すぐに続けて行うものとする）。</summary>
        private static List<TimedStep> Schedule(AgentPose start, long startTime, IReadOnlyList<AgentAction> actions, AgentMotion motion)
        {
            var steps = new List<TimedStep>();
            var pose = start;
            var time = startTime;
            foreach (var action in actions)
            {
                if (action is MoveAction move && move.From != pose.NodeId || action is RotateAction rotate && rotate.NodeId != pose.NodeId)
                {
                    throw new ArgumentException($"すでに渡した動作が、AGV の位置（{pose.NodeId}）からつながっていません。", nameof(actions));
                }

                var end = time + MotionGraph.DurationTicks(action, pose.Heading, motion);
                steps.Add(new TimedStep(action, time, end));
                pose = After(pose, new TimedStep(action, time, end));
                time = end;
            }

            return steps;
        }

        private static AgentPose After(AgentPose pose, TimedStep step) => step.Action switch
        {
            MoveAction move => new AgentPose(move.To, pose.Heading),
            RotateAction rotate => new AgentPose(rotate.NodeId, rotate.ToHeading),
            _ => pose,
        };
    }

    /// <summary>計画の途中の、AGV ごとの様子。</summary>
    /// <param name="Pose">すでに渡した動作を終えたあとの姿勢。</param>
    /// <param name="EndTime">すでに渡した動作を終える時刻。</param>
    /// <param name="OccupiedSince">今いる（渡した動作を終えたあとの）ノードを使い始めた時刻。</param>
    private sealed record AgentState(PlanningAgent Agent, IReadOnlyList<TimedStep> Committed, AgentPose Pose, long EndTime, long OccupiedSince)
    {
        public PlanStatus? Status { get; set; }

        public IReadOnlyList<TimedStep> Planned { get; set; } = [];
    }
}
