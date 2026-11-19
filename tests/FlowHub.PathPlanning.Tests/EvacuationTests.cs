namespace FlowHub.PathPlanning.Tests;

/// <summary>
/// 通り道を塞いでいる、行き先のない動かせる AGV を、待機パネルへどかすテスト。
/// </summary>
public class EvacuationTests
{
    private static readonly AgentMotion Motion = new(500, 250, 90);

    private static PlanningNode Normal(string id) => new(id, CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

    private static PlanningNode Standby(string id) => new(id, CanRotate: false, ForwardEntryOnly: true, BackwardExitOnly: true, IsStandby: true);

    private static PlanningNode Transfer(string id) => new(id, CanRotate: false, ForwardEntryOnly: true, BackwardExitOnly: true);

    private static PlanningEdge Edge(string from, string to, int heading, int distance = 800) => new(from, to, distance, heading, OneWay: false);

    private static PlanningAgent Mover(string id, string node, int heading, string goal) =>
        new(id, new AgentPose(node, heading), Motion, new AgentGoal(goal, Heading: null), Committed: [], Movable: false);

    private static PlanningAgent Idle(string id, string node, int heading, bool movable = true) =>
        new(id, new AgentPose(node, heading), Motion, Goal: null, Committed: [], Movable: movable);

    private static PlanningResult Plan(PlanningMap map, params PlanningAgent[] agents) =>
        new PrioritizedPlanner().Plan(new PlanningRequest(map, agents, Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));

    private static AgentPlan PlanOf(PlanningResult result, string agent) => result.Plans.Single(p => p.AgentId == agent);

    /// <summary>
    /// N →(180度) M →(180度) S の通路。M の東（90度）に待機パネル P1、西（270度）に待機パネル P2（P1 より遠い）。
    /// </summary>
    private static PlanningMap Corridor(bool farStandbyOnly = false) =>
        new(
            [Normal("N"), Normal("M"), Normal("S"), Standby("P1"), Standby("P2")],
            [
                Edge("N", "M", 180),
                Edge("M", "S", 180),
                Edge("M", "P1", 90, distance: farStandbyOnly ? 2000 : 800),
                Edge("M", "P2", 270, distance: 1200),
            ]);

    [Fact]
    public void 通り道を塞いでいる動かせるAGVは一番近い空いている待機パネルへどかしてから通る()
    {
        var result = Plan(Corridor(), Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90));

        var evacuated = PlanOf(result, "AGV-2");
        Assert.Equal(PlanStatus.Planned, evacuated.Status);
        Assert.Equal(new MoveAction(AgentDirection.Forward, "M", "P1", 800), evacuated.Actions[^1].Action);
        Assert.Equal(PlanStatus.Planned, PlanOf(result, "AGV-1").Status);
        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 一番近い待機パネルは移動コストで測る()
    {
        // P1 は 2000mm 先、P2 は 1200mm 先（ただし向きを変える回転が要る）。所要時間で比べる
        var result = Plan(Corridor(farStandbyOnly: true), Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90));

        // P1：前進 2000mm = 4秒。P2：180度の回転 2秒 ＋ 前進 1200mm 2.4秒 = 4.4秒。P1 のほうが近い
        Assert.Equal(new MoveAction(AgentDirection.Forward, "M", "P1", 2000), PlanOf(result, "AGV-2").Actions[^1].Action);
    }

    [Fact]
    public void 動かせないAGVはどかさない()
    {
        var result = Plan(Corridor(), Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90, movable: false));

        Assert.Equal(PlanStatus.Stays, PlanOf(result, "AGV-2").Status);
        Assert.Equal(PlanStatus.Blocked, PlanOf(result, "AGV-1").Status);
    }

    [Fact]
    public void ほかのAGVが止まり続ける待機パネルは使わない()
    {
        // P1 には AGV-3 が止まっている。AGV-2 は P2 へどく
        var result = Plan(Corridor(), Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90), Idle("AGV-3", "P1", 90, movable: false));

        var evacuated = PlanOf(result, "AGV-2");
        Assert.Equal("P2", ((MoveAction)evacuated.Actions[^1].Action).To);
    }

    [Fact]
    public void ほかのAGVの行き先の待機パネルは使わない()
    {
        // AGV-3 が P1 へ向かう。AGV-2 は P2 へどく
        var map = new PlanningMap(
            [Normal("N"), Normal("M"), Normal("S"), Normal("E"), Standby("P1"), Standby("P2")],
            [Edge("N", "M", 180), Edge("M", "S", 180), Edge("M", "P1", 90), Edge("M", "P2", 270, distance: 1200), Edge("S", "E", 90)]);

        var result = Plan(map, Mover("AGV-3", "S", 0, goal: "P1"), Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90));

        Assert.Equal("P2", ((MoveAction)PlanOf(result, "AGV-2").Actions[^1].Action).To);
    }

    [Fact]
    public void 空いている待機パネルがなければどかさず塞がれていて行けないとする()
    {
        var map = new PlanningMap([Normal("N"), Normal("M"), Normal("S")], [Edge("N", "M", 180), Edge("M", "S", 180)]);

        var result = Plan(map, Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90));

        Assert.Equal(PlanStatus.Stays, PlanOf(result, "AGV-2").Status);
        Assert.Equal(PlanStatus.Blocked, PlanOf(result, "AGV-1").Status);
    }

    [Fact]
    public void 受け渡しパネルにはどかさない()
    {
        var map = new PlanningMap(
            [Normal("N"), Normal("M"), Normal("S"), Transfer("T")],
            [Edge("N", "M", 180), Edge("M", "S", 180), Edge("M", "T", 90)]);

        var result = Plan(map, Mover("AGV-1", "N", 180, goal: "S"), Idle("AGV-2", "M", 90));

        Assert.Equal(PlanStatus.Stays, PlanOf(result, "AGV-2").Status);
    }

    [Fact]
    public void 塞いでいなければどかさない()
    {
        var result = Plan(Corridor(), Mover("AGV-1", "N", 180, goal: "M"), Idle("AGV-2", "S", 0));

        Assert.Equal(PlanStatus.Stays, PlanOf(result, "AGV-2").Status);
        Assert.Empty(PlanOf(result, "AGV-2").Actions);
    }
}
