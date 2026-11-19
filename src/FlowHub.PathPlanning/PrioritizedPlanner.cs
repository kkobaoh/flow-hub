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
/// <item>繰り上げる AGV が、行き先がなく動かせるなら、移動コストが一番近い、空いている待機パネルへどかす（同じ近さなら ID の順）。
/// 空いている待機パネルがなければ、どかさない。</item>
/// <item>計画できなければ、マップの上に経路がないなら「経路がない」、あるなら「塞がれていて行けない」とし、今いるノードに止まり続けるものとする。</item>
/// </list>
/// </remarks>
/// <param name="clock">改善にかける時間を測る時計。省略したら、システムの時計。</param>
public sealed class PrioritizedPlanner(TimeProvider? clock = null) : IPathPlanner
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// 決めた順番で止まらない計画を立て、改善にかけてよい時間があれば、順番を入れ替えて改善する。
    /// </summary>
    /// <remarks>
    /// 改善は、順番の隣り合う2台の入れ替えを、前から決まった順に試し、よくなれば採用して続ける。よくならなくなるか、
    /// 時間の上限が来たら止める。よさは、計画できた AGV の数（多いほど）、必要時刻に対する遅れの合計（少ないほど）、
    /// 到着時刻の合計（少ないほど）の順に比べる。結果の並びは、頼みの並び順のまま。
    /// </remarks>
    public PlanningResult Plan(PlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var best = new Run(request).Execute();
        if (request.ImprovementBudget <= TimeSpan.Zero || request.Agents.Count < 2)
        {
            return best;
        }

        var started = _clock.GetTimestamp();
        bool TimeIsUp() => _clock.GetElapsedTime(started) >= request.ImprovementBudget;

        var order = request.Agents.ToList();
        var bestScore = Score.Of(best, request.Agents);
        var improved = true;
        while (improved && !TimeIsUp())
        {
            improved = false;
            for (var index = 0; index + 1 < order.Count && !TimeIsUp(); index++)
            {
                if (order[index].Goal is null && order[index + 1].Goal is null)
                {
                    continue; // 行き先のない AGV どうしは、入れ替えても変わらない
                }

                var candidateOrder = order.ToList();
                (candidateOrder[index], candidateOrder[index + 1]) = (candidateOrder[index + 1], candidateOrder[index]);
                var candidate = new Run(new PlanningRequest(request.Map, candidateOrder, request.Obstacles, TimeSpan.Zero, request.Horizon)).Execute();
                var score = Score.Of(candidate, request.Agents);
                if (score.CompareTo(bestScore) < 0)
                {
                    (best, bestScore, order, improved) = (candidate, score, candidateOrder, true);
                }
            }
        }

        // 結果の並びは、頼みの並び順にそろえる
        var position = request.Agents.Select((agent, index) => (agent.Id, index)).ToDictionary(x => x.Id, x => x.index);
        return best with { Plans = best.Plans.OrderBy(plan => position[plan.AgentId]).ToArray() };
    }

    /// <summary>
    /// 計画のよさ。計画できなかった AGV の数、必要時刻に対する遅れの合計、到着時刻の合計（すべて小さいほどよい）。
    /// </summary>
    private readonly record struct Score(int NotPlanned, long LatenessTicks, long ArrivalTicks) : IComparable<Score>
    {
        public int CompareTo(Score other) =>
            NotPlanned != other.NotPlanned ? NotPlanned.CompareTo(other.NotPlanned)
            : LatenessTicks != other.LatenessTicks ? LatenessTicks.CompareTo(other.LatenessTicks)
            : ArrivalTicks.CompareTo(other.ArrivalTicks);

        public static Score Of(PlanningResult result, IReadOnlyList<PlanningAgent> agents)
        {
            var byId = agents.ToDictionary(agent => agent.Id);
            var notPlanned = 0;
            long lateness = 0;
            long arrivals = 0;
            foreach (var plan in result.Plans)
            {
                var agent = byId[plan.AgentId];
                if (agent.Goal is not null && plan.Status != PlanStatus.Planned)
                {
                    notPlanned++;
                    continue;
                }

                if (plan.Status != PlanStatus.Planned || plan.Actions.Count == 0)
                {
                    continue;
                }

                var arrival = plan.Actions[^1].End;
                arrivals += arrival.Ticks;
                if (agent.Due is { } due && arrival > due)
                {
                    lateness += (arrival - due).Ticks;
                }
            }

            return new Score(notPlanned, lateness, arrivals);
        }
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
        private readonly HashSet<string> _goals;

        public Run(PlanningRequest request)
        {
            _request = request;
            _graph = new MotionGraph(request.Map);
            _horizon = request.Horizon.Ticks;
            _goals = request.Agents.Select(a => a.Goal?.NodeId).OfType<string>().ToHashSet();
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

        /// <param name="evacuate">通り道を塞いでいるので繰り上げた。行き先がなく動かせるなら、待機パネルへどかす。</param>
        private void PlanAgent(AgentState state, bool evacuate = false)
        {
            var agent = state.Agent;
            _occupancy.RemoveProvisional(agent.Id);
            if (agent.Goal is not { } goal)
            {
                if (!(evacuate && agent.Movable && TryEvacuate(state)))
                {
                    Settle(state, PlanStatus.Stays, []);
                }

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
                    PlanAgent(_agents[blocker], evacuate: true);
                }
            }

            _occupancy.RemoveProvisional(state.Agent.Id);
            _inProgress.Remove(state.Agent.Id);
            return true;
        }

        /// <summary>
        /// 移動コストが一番近い、空いている待機パネルへどかす。空いているとは、どの AGV も使わず、誰の行き先でもないこと。
        /// </summary>
        /// <returns>どかす計画ができたら true。</returns>
        private bool TryEvacuate(AgentState state)
        {
            var best = _request.Map.Nodes
                .Where(node => node.IsStandby && node.Id != state.Pose.NodeId && !_goals.Contains(node.Id) && _occupancy.IsUnused(node.Id))
                .Select(node => (Node: node.Id, Steps: Search(state, new AgentGoal(node.Id, Heading: null), ignoreProvisional: false)))
                .Where(candidate => candidate.Steps is not null)
                .OrderBy(candidate => candidate.Steps!.Count > 0 ? candidate.Steps[^1].End : state.EndTime)
                .ThenBy(candidate => candidate.Node, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best.Steps is null)
            {
                return false;
            }

            Settle(state, PlanStatus.Planned, best.Steps);
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
