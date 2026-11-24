namespace FlowHub.PathPlanning.Tests;

/// <summary>
/// AGV ごとの「入れないノード」（走れないパネル）のテスト。
/// </summary>
/// <remarks>
/// A から B へ、X を通る近道（800mm + 800mm）と、C、D を回る遠回り。どれも北（0度）向きに進めるように、つながりの向きをそろえている。
/// A →(0度) X →(0度) B、A →(90度) C →(0度) D →(270度) B。
/// </remarks>
public class ForbiddenNodeTests
{
    private static readonly AgentMotion Motion = new(500, 250, 90);

    private static PlanningNode Normal(string id) => new(id, CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

    private static PlanningNode Standby(string id) => new(id, CanRotate: false, ForwardEntryOnly: true, BackwardExitOnly: true, IsStandby: true);

    private static PlanningEdge Edge(string from, string to, int heading) => new(from, to, 800, heading, OneWay: false);

    private static readonly PlanningMap ShortcutAndDetour = new(
        [Normal("A"), Normal("X"), Normal("B"), Normal("C"), Normal("D")],
        [Edge("A", "X", 0), Edge("X", "B", 0), Edge("A", "C", 90), Edge("C", "D", 0), Edge("D", "B", 270)]);

    private static PlanningAgent Agent(string id, string node, int heading, string? goal, params string[] forbidden) =>
        new(id, new AgentPose(node, heading), Motion, goal is null ? null : new AgentGoal(goal, Heading: null), Committed: [], Movable: goal is null)
        {
            ForbiddenNodes = forbidden.ToHashSet(),
        };

    private static PlanningResult Plan(PlanningMap map, params PlanningAgent[] agents) =>
        new PrioritizedPlanner().Plan(new PlanningRequest(map, agents, Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));

    private static string[] MovesTo(AgentPlan plan) => plan.Actions.Select(a => a.Action).OfType<MoveAction>().Select(m => m.To).ToArray();

    [Fact]
    public void 入れないノードがなければ近道を通る()
    {
        var plan = Assert.Single(Plan(ShortcutAndDetour, Agent("AGV-1", "A", 0, "B")).Plans);

        Assert.Equal(["X", "B"], MovesTo(plan));
    }

    [Fact]
    public void 入れないノードは通らずに遠回りする()
    {
        var plan = Assert.Single(Plan(ShortcutAndDetour, Agent("AGV-1", "A", 0, "B", forbidden: "X")).Plans);

        Assert.Equal(PlanStatus.Planned, plan.Status);
        Assert.DoesNotContain("X", MovesTo(plan));
        Assert.Equal("B", MovesTo(plan)[^1]);
    }

    [Fact]
    public void 入れないノードを通らなければ行けないなら経路なし()
    {
        var plan = Assert.Single(Plan(ShortcutAndDetour, Agent("AGV-1", "A", 0, "B", forbidden: ["X", "C"])).Plans);

        Assert.Equal(PlanStatus.NoRoute, plan.Status);
    }

    [Fact]
    public void 入れないノードへは行けない()
    {
        var plan = Assert.Single(Plan(ShortcutAndDetour, Agent("AGV-1", "A", 0, "X", forbidden: "X")).Plans);

        Assert.Equal(PlanStatus.NoRoute, plan.Status);
    }

    [Fact]
    public void 入れないノードにいるAGVもそこから出られる()
    {
        var plan = Assert.Single(Plan(ShortcutAndDetour, Agent("AGV-1", "X", 0, "B", forbidden: "X")).Plans);

        Assert.Equal(["B"], MovesTo(plan));
    }

    [Fact]
    public void 待機パネルへどかすときも入れないノードは使わない()
    {
        // N →(180度) M →(180度) S の通路の M に、行き先のない AGV-2。M の東に待機パネル P1（AGV-2 は入れない）、西に P2
        var map = new PlanningMap(
            [Normal("N"), Normal("M"), Normal("S"), Standby("P1"), Standby("P2")],
            [Edge("N", "M", 180), Edge("M", "S", 180), Edge("M", "P1", 90), Edge("M", "P2", 270)]);

        var result = Plan(map, Agent("AGV-1", "N", 180, "S"), Agent("AGV-2", "M", 90, goal: null, forbidden: "P1"));

        Assert.Equal("P2", MovesTo(result.Plans[1])[^1]);
    }
}
