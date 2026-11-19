namespace FlowHub.PathPlanning.Tests;

/// <summary>
/// 止まらない計画（順番に、時刻つきで先の AGV を避けて経路を決める）のテスト。
/// 速さは、前進 500mm/秒、後退 250mm/秒、回転 90度/秒。800mm 進むのに 1.6秒かかる。
/// </summary>
public class PrioritizedPlannerTests
{
    private static readonly AgentMotion Motion = new(500, 250, 90);
    private static readonly TimeSpan Horizon = TimeSpan.FromMinutes(10);

    private static PlanningNode Normal(string id) => new(id, CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

    private static PlanningNode DeadEnd(string id) => new(id, CanRotate: false, ForwardEntryOnly: true, BackwardExitOnly: true);

    private static PlanningEdge Edge(string from, string to, int heading, int distance = 800, bool oneWay = false) =>
        new(from, to, distance, heading, oneWay);

    private static PlanningAgent Agent(string id, string node, int heading, string? goal = null, int? goalHeading = null, IReadOnlyList<AgentAction>? committed = null) =>
        new(id, new AgentPose(node, heading), Motion, goal is null ? null : new AgentGoal(goal, goalHeading), committed ?? [], Movable: false);

    private static PlanningResult Plan(PlanningMap map, IReadOnlyList<PlanningAgent> agents, IReadOnlyList<PlanningObstacle>? obstacles = null, TimeSpan? horizon = null) =>
        new PrioritizedPlanner().Plan(new PlanningRequest(map, agents, obstacles ?? [], ImprovementBudget: TimeSpan.Zero, horizon ?? Horizon));

    private static AgentPlan PlanOf(PlanningResult result, string agent) => result.Plans.Single(p => p.AgentId == agent);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    /// <summary>A →(0度) B →(0度) T（行き止まり）。</summary>
    private static PlanningMap Line() =>
        new([Normal("A"), Normal("B"), DeadEnd("T")], [Edge("A", "B", 0), Edge("B", "T", 0)]);

    /// <summary>
    /// 十字路。N →(180度) M →(180度) S と、E →(270度) M →(270度) W が、M で交わる。
    /// </summary>
    private static PlanningMap Crossroads() =>
        new(
            [Normal("N"), Normal("M"), Normal("S"), Normal("E"), Normal("W")],
            [Edge("N", "M", 180), Edge("M", "S", 180), Edge("E", "M", 270), Edge("M", "W", 270)]);

    // ---- 1台 ----

    [Fact]
    public void 一台なら一番早い経路で行き先に着く()
    {
        var result = Plan(Line(), [Agent("AGV-1", "A", 0, goal: "T")]);

        var plan = PlanOf(result, "AGV-1");
        Assert.Equal(PlanStatus.Planned, plan.Status);
        Assert.Equal(
            [
                new TimedAction(new MoveAction(AgentDirection.Forward, "A", "B", 800), Seconds(0), Seconds(1.6)),
                new TimedAction(new MoveAction(AgentDirection.Forward, "B", "T", 800), Seconds(1.6), Seconds(3.2)),
            ],
            plan.Actions);
        Assert.Equal(["AGV-1"], result.PassingOrder["B"]);
    }

    [Fact]
    public void ゴールの向きを指定したらその向きで着く()
    {
        var result = Plan(Line(), [Agent("AGV-1", "A", 0, goal: "B", goalHeading: 90)]);

        Assert.Equal(
            [
                new TimedAction(new MoveAction(AgentDirection.Forward, "A", "B", 800), Seconds(0), Seconds(1.6)),
                new TimedAction(new RotateAction("B", 90), Seconds(1.6), Seconds(2.6)),
            ],
            PlanOf(result, "AGV-1").Actions);
    }

    [Fact]
    public void すでに渡した動作は動かさずその続きから計画する()
    {
        var committed = new AgentAction[] { new MoveAction(AgentDirection.Forward, "A", "B", 800) };

        var result = Plan(Line(), [Agent("AGV-1", "A", 0, goal: "T", committed: committed)]);

        Assert.Equal(
            [
                new TimedAction(new MoveAction(AgentDirection.Forward, "A", "B", 800), Seconds(0), Seconds(1.6)),
                new TimedAction(new MoveAction(AgentDirection.Forward, "B", "T", 800), Seconds(1.6), Seconds(3.2)),
            ],
            PlanOf(result, "AGV-1").Actions);
    }

    [Fact]
    public void 行き先のないAGVは動かさずそのパネルに止まり続ける()
    {
        var result = Plan(Line(), [Agent("AGV-1", "B", 0)]);

        var plan = PlanOf(result, "AGV-1");
        Assert.Equal(PlanStatus.Stays, plan.Status);
        Assert.Empty(plan.Actions);
        Assert.Equal(["AGV-1"], result.PassingOrder["B"]);
    }

    // ---- 複数台 ----

    [Fact]
    public void 先のAGVが通り過ぎるのを待ってから通り通る順番を返す()
    {
        // AGV-1 は N→M→S で、M を 0〜3.2秒に使う。AGV-2 は、E で待ってから M に入る
        var result = Plan(Crossroads(), [Agent("AGV-1", "N", 180, goal: "S"), Agent("AGV-2", "E", 270, goal: "W")]);

        Assert.Equal(
            [
                new TimedAction(new MoveAction(AgentDirection.Forward, "E", "M", 800), Seconds(3.2), Seconds(4.8)),
                new TimedAction(new MoveAction(AgentDirection.Forward, "M", "W", 800), Seconds(4.8), Seconds(6.4)),
            ],
            PlanOf(result, "AGV-2").Actions);
        Assert.Equal(["AGV-1", "AGV-2"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 順番は頼みの並び順で決まる()
    {
        var result = Plan(Crossroads(), [Agent("AGV-2", "E", 270, goal: "W"), Agent("AGV-1", "N", 180, goal: "S")]);

        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 順番があとのAGVが通り道にいたら繰り上げてどかしてから通る()
    {
        // A → B → C → D → E の1本道。先の AGV-1（A から D へ）の通り道の B に、あとの AGV-2（B から E へ）がいる
        var map = new PlanningMap(
            [Normal("A"), Normal("B"), Normal("C"), Normal("D"), Normal("E")],
            [Edge("A", "B", 0), Edge("B", "C", 0), Edge("C", "D", 0), Edge("D", "E", 0)]);

        var result = Plan(map, [Agent("AGV-1", "A", 0, goal: "D"), Agent("AGV-2", "B", 0, goal: "E")]);

        Assert.Equal(PlanStatus.Planned, PlanOf(result, "AGV-1").Status);
        Assert.Equal(PlanStatus.Planned, PlanOf(result, "AGV-2").Status);
        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["B"]);
        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["D"]);
    }

    [Fact]
    public void 一本道で向かい合ってどちらもどけないときは塞がれていて行けないとする()
    {
        var map = new PlanningMap([Normal("A"), Normal("B"), Normal("C")], [Edge("A", "B", 0), Edge("B", "C", 0)]);

        var result = Plan(map, [Agent("AGV-1", "A", 0, goal: "C"), Agent("AGV-2", "C", 180, goal: "A")]);

        Assert.Equal(PlanStatus.Blocked, PlanOf(result, "AGV-1").Status);
        Assert.Equal(PlanStatus.Blocked, PlanOf(result, "AGV-2").Status);
        Assert.Empty(PlanOf(result, "AGV-1").Actions);
    }

    // ---- 計画が立てられないとき ----

    [Fact]
    public void マップの上に経路がなければ経路がないとする()
    {
        var map = new PlanningMap([Normal("A"), Normal("B"), Normal("X")], [Edge("A", "B", 0)]);

        var result = Plan(map, [Agent("AGV-1", "A", 0, goal: "X")]);

        Assert.Equal(PlanStatus.NoRoute, PlanOf(result, "AGV-1").Status);
    }

    [Fact]
    public void 動かせないAGVに塞がれていれば塞がれていて行けないとする()
    {
        var result = Plan(Line(), [Agent("AGV-1", "A", 0, goal: "T")], obstacles: [new PlanningObstacle("AGV-9", ["B"])]);

        Assert.Equal(PlanStatus.Blocked, PlanOf(result, "AGV-1").Status);
        Assert.Equal(["AGV-9"], result.PassingOrder["B"]);
    }

    [Fact]
    public void 計画する時間の長さの上限までに着けなければ塞がれていて行けないとする()
    {
        var result = Plan(Line(), [Agent("AGV-1", "A", 0, goal: "T")], horizon: TimeSpan.FromSeconds(2));

        Assert.Equal(PlanStatus.Blocked, PlanOf(result, "AGV-1").Status);
    }

    // ---- 動き方の決まり ----

    [Fact]
    public void 行き止まりのパネルには前進でしか入れない()
    {
        // B（180度向き）から T へは、後退で入れないので、回転してから前進で入る
        var result = Plan(Line(), [Agent("AGV-1", "B", 180, goal: "T")]);

        Assert.Equal(
            [
                new TimedAction(new RotateAction("B", 0), Seconds(0), Seconds(2)),
                new TimedAction(new MoveAction(AgentDirection.Forward, "B", "T", 800), Seconds(2), Seconds(3.6)),
            ],
            PlanOf(result, "AGV-1").Actions);
    }

    [Fact]
    public void 一方通行は後退でも逆向きには通れない()
    {
        var map = new PlanningMap([Normal("A"), Normal("B")], [Edge("A", "B", 0, oneWay: true)]);

        var result = Plan(map, [Agent("AGV-1", "B", 0, goal: "A")]);

        Assert.Equal(PlanStatus.NoRoute, PlanOf(result, "AGV-1").Status);
    }
}
