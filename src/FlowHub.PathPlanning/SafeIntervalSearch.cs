namespace FlowHub.PathPlanning;

/// <summary>時刻つきの動作（tick）。</summary>
internal readonly record struct TimedStep(AgentAction Action, long Start, long End);

/// <summary>
/// 1台の、時刻つきの経路探索。ノードごとの「空いている時間帯」をもとに、先の AGV を避け、必要ならその場で待って進む。
/// </summary>
/// <remarks>
/// AGV が使うノード：止まっている（待っている）ときと回転中は、そのノード。隣へ前進・後退しているときは、出発したノードと着くノードの両方。
/// 一番早く着く経路を選び、同じ時刻なら回転の少ないほう、それも同じなら後退の少ないほうを選ぶ。
/// ゴールでは、そのあとずっと止まっていられる（空いている時間帯が終わらない）ことを求める。
/// </remarks>
internal static class SafeIntervalSearch
{
    public static IReadOnlyList<TimedStep>? Search(
        MotionGraph graph,
        Occupancy occupancy,
        bool ignoreProvisional,
        AgentMotion motion,
        AgentPose start,
        long startTime,
        AgentGoal goal,
        long horizon)
    {
        var free = new Dictionary<string, IReadOnlyList<(long Start, long End)>>();
        IReadOnlyList<(long Start, long End)> FreeAt(string node)
        {
            if (!free.TryGetValue(node, out var intervals))
            {
                intervals = occupancy.FreeIntervals(node, ignoreProvisional);
                free[node] = intervals;
            }

            return intervals;
        }

        var startInterval = IndexOf(FreeAt(start.NodeId), startTime);
        if (startInterval < 0)
        {
            return null;
        }

        var origin = new State(start.NodeId, start.Heading, startInterval);
        var best = new Dictionary<State, Label> { [origin] = new Label(startTime, 0, 0) };
        var parent = new Dictionary<State, (State Previous, TimedStep Step)>();
        var queue = new PriorityQueue<State, Label>();
        queue.Enqueue(origin, best[origin]);

        while (queue.TryDequeue(out var state, out var label))
        {
            if (best[state].CompareTo(label) < 0)
            {
                continue; // よりよい到着で、すでに処理済み
            }

            var interval = FreeAt(state.Node)[state.Interval];
            if (state.Node == goal.NodeId && (goal.Heading is null || goal.Heading == state.Heading) && interval.End == Occupancy.Forever)
            {
                return Reconstruct(state, parent);
            }

            // その場での回転（同じ空いている時間帯の中で）
            foreach (var heading in graph.RotationTargets(state.Node, state.Heading, goal))
            {
                var rotate = new RotateAction(state.Node, heading);
                var end = label.Time + MotionGraph.DurationTicks(rotate, state.Heading, motion);
                if (end <= interval.End && end <= horizon)
                {
                    Push(new State(state.Node, heading, state.Interval), label with { Time = end, Rotations = label.Rotations + 1 }, state, new TimedStep(rotate, label.Time, end));
                }
            }

            // 隣のノードへの移動（必要なら、その場で待ってから）
            foreach (var move in graph.MovesFrom(state.Node, state.Heading))
            {
                var duration = MotionGraph.DurationTicks(move, state.Heading, motion);
                var target = FreeAt(move.To);
                for (var index = 0; index < target.Count; index++)
                {
                    var (targetStart, targetEnd) = target[index];
                    if (targetEnd <= label.Time)
                    {
                        continue;
                    }

                    var departure = Math.Max(label.Time, targetStart);
                    var arrival = departure + duration;
                    if (arrival > interval.End || arrival > horizon)
                    {
                        break; // 今のノードに、それ以上はいられない（あとの時間帯ほど遅くなる）
                    }

                    if (arrival <= targetEnd)
                    {
                        var backwards = label.Backwards + (move.Direction == AgentDirection.Backward ? 1 : 0);
                        Push(new State(move.To, state.Heading, index), new Label(arrival, label.Rotations, backwards), state, new TimedStep(move, departure, arrival));
                    }
                }
            }
        }

        return null;

        void Push(State next, Label nextLabel, State previous, TimedStep step)
        {
            if (best.TryGetValue(next, out var known) && known.CompareTo(nextLabel) <= 0)
            {
                return;
            }

            best[next] = nextLabel;
            parent[next] = (previous, step);
            queue.Enqueue(next, nextLabel);
        }
    }

    private static int IndexOf(IReadOnlyList<(long Start, long End)> intervals, long time)
    {
        for (var index = 0; index < intervals.Count; index++)
        {
            if (intervals[index].Start <= time && time < intervals[index].End)
            {
                return index;
            }
        }

        return -1;
    }

    private static List<TimedStep> Reconstruct(State goal, Dictionary<State, (State Previous, TimedStep Step)> parent)
    {
        var steps = new List<TimedStep>();
        var current = goal;
        while (parent.TryGetValue(current, out var link))
        {
            steps.Add(link.Step);
            current = link.Previous;
        }

        steps.Reverse();
        return steps;
    }

    /// <summary>探索の状態（ノード、向き、そのノードの何番目の空いている時間帯にいるか）。</summary>
    private readonly record struct State(string Node, int Heading, int Interval);

    /// <summary>着いた時刻と、それまでの回転と後退の数。早い時刻、少ない回転、少ない後退の順によい。</summary>
    private readonly record struct Label(long Time, int Rotations, int Backwards) : IComparable<Label>
    {
        public int CompareTo(Label other) =>
            Time != other.Time ? Time.CompareTo(other.Time)
            : Rotations != other.Rotations ? Rotations.CompareTo(other.Rotations)
            : Backwards.CompareTo(other.Backwards);
    }
}
