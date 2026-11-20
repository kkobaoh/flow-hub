namespace FlowHub.PathPlanning.Tests;

/// <summary>
/// 着く時刻が同じ経路の選び方（回転の少ないほう、それも同じなら後退の少ないほう）のテスト。どちらも回数で数える。
/// </summary>
public class TieBreakTests
{
    // 前進 500mm/s、後退 250mm/s、回転 90度/s
    private static readonly AgentMotion Motion = new(500, 250, 90);

    private static PlanningNode Normal(string id) => new(id, CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

    private static PlanningEdge Edge(string from, string to, int heading, int distanceMm = 800) => new(from, to, distanceMm, heading, OneWay: false);

    private static AgentPlan PlanOne(PlanningNode[] nodes, IEnumerable<PlanningEdge> edges, string goal)
    {
        var agent = new PlanningAgent("AGV-1", new AgentPose("S", 0), Motion, new AgentGoal(goal, Heading: null), Committed: [], Movable: false);
        var result = new PrioritizedPlanner().Plan(new PlanningRequest(new PlanningMap(nodes, edges), [agent], Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));
        return Assert.Single(result.Plans);
    }

    private static string[] MovesTo(AgentPlan plan) => plan.Actions.Select(a => a.Action).OfType<MoveAction>().Select(m => m.To).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 着く時刻が同じなら回転の少ない経路を選ぶ(bool reverseEdgeOrder)
    {
        // S（北向き）から G へ。180度回って S → M（1600mm）→ G（800mm）と前進するか、90度ずつ2回回って S → E（800mm）→ G（1600mm）と前進するか
        // （どちらも 180度、2400mm で 6.8秒）。回転の多い経路のほうが、先に G の手前まで着く
        PlanningEdge[] edges = [Edge("S", "M", 180, distanceMm: 1600), Edge("M", "G", 180), Edge("S", "E", 90), Edge("E", "G", 180, distanceMm: 1600)];

        var plan = PlanOne([Normal("S"), Normal("M"), Normal("E"), Normal("G")], reverseEdgeOrder ? edges.Reverse() : edges, "G");

        Assert.Single(plan.Actions.Select(a => a.Action).OfType<RotateAction>());
        Assert.Equal(["M", "G"], MovesTo(plan));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 着く時刻も回転の回数も同じなら後退の少ない経路を選ぶ(bool reverseEdgeOrder)
    {
        // S（北向き）から G へ。S → X → G と 800mm ずつ前進するか、S → Y へ 400mm 後退して Y → G へ 800mm 前進するか（どちらも 3.2秒、回転なし）
        PlanningEdge[] edges = [Edge("S", "X", 0), Edge("X", "G", 0), Edge("S", "Y", 180, distanceMm: 400), Edge("Y", "G", 0)];

        var plan = PlanOne([Normal("S"), Normal("X"), Normal("Y"), Normal("G")], reverseEdgeOrder ? edges.Reverse() : edges, "G");

        Assert.All(plan.Actions.Select(a => a.Action).OfType<MoveAction>(), m => Assert.Equal(AgentDirection.Forward, m.Direction));
        Assert.Equal(["X", "G"], MovesTo(plan));
    }
}
