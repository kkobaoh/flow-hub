namespace FlowHub.PathPlanning.Tests;

/// <summary>
/// 計画の改善（順番を入れ替えて、必要時刻に対する遅れの合計、同じなら到着時刻の合計を縮める）のテスト。
/// </summary>
/// <remarks>
/// 十字路：N2 →(180度) N →(180度) M →(180度) S と、E →(270度) M →(270度) W。
/// AGV-1 は N2 から S へ（M まで2枚）、AGV-2 は E から W へ（M まで1枚）。800mm 進むのに 1.6秒。
/// 決めた順番（AGV-1 が先）なら、到着は AGV-1 が 4.8秒、AGV-2 が 8.0秒（合計 12.8秒）。
/// 入れ替える（AGV-2 が先）と、AGV-2 が 3.2秒、AGV-1 が 6.4秒（合計 9.6秒）。
/// </remarks>
public class PlanImprovementTests
{
    private static readonly AgentMotion Motion = new(500, 250, 90);

    private static PlanningNode Normal(string id) => new(id, CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

    private static PlanningEdge Edge(string from, string to, int heading) => new(from, to, 800, heading, OneWay: false);

    private static readonly PlanningMap Crossroads = new(
        [Normal("N2"), Normal("N"), Normal("M"), Normal("S"), Normal("E"), Normal("W")],
        [Edge("N2", "N", 180), Edge("N", "M", 180), Edge("M", "S", 180), Edge("E", "M", 270), Edge("M", "W", 270)]);

    private static PlanningAgent Agent(string id, string node, int heading, string goal, double? dueSeconds) =>
        new(id, new AgentPose(node, heading), Motion, new AgentGoal(goal, Heading: null), Committed: [], Movable: false)
        {
            Due = dueSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        };

    private static PlanningResult Plan(TimeSpan budget, params PlanningAgent[] agents) =>
        new PrioritizedPlanner().Plan(new PlanningRequest(Crossroads, agents, Obstacles: [], ImprovementBudget: budget, Horizon: TimeSpan.FromMinutes(10)));

    private static readonly TimeSpan Enough = TimeSpan.FromMinutes(1);

    [Fact]
    public void 遅れが増えないなら到着時刻の合計が小さくなるように順番を入れ替える()
    {
        var result = Plan(Enough, Agent("AGV-1", "N2", 180, "S", dueSeconds: 100), Agent("AGV-2", "E", 270, "W", dueSeconds: 100));

        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 入れ替えると遅れの合計が増えるなら入れ替えない()
    {
        // AGV-1 は 4.8秒までに必要。入れ替えると 6.4秒になって遅れる
        var result = Plan(Enough, Agent("AGV-1", "N2", 180, "S", dueSeconds: 4.8), Agent("AGV-2", "E", 270, "W", dueSeconds: 100));

        Assert.Equal(["AGV-1", "AGV-2"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 遅れの合計が減るなら必要時刻が早いAGVよりあとに回すこともある()
    {
        // 決めた順番では、AGV-2 が 8.0秒で着き、5秒の必要時刻に 3秒遅れる。
        // 入れ替えると、AGV-2 は 3.2秒で間に合い、AGV-1 は 6.4秒で、6秒の必要時刻に 0.4秒遅れる。遅れの合計が減る
        var result = Plan(Enough, Agent("AGV-1", "N2", 180, "S", dueSeconds: 6), Agent("AGV-2", "E", 270, "W", dueSeconds: 5));

        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 必要時刻のない移動は到着時刻の合計で比べる()
    {
        var result = Plan(Enough, Agent("AGV-1", "N2", 180, "S", dueSeconds: null), Agent("AGV-2", "E", 270, "W", dueSeconds: null));

        Assert.Equal(["AGV-2", "AGV-1"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 改善にかける時間が0なら入れ替えない()
    {
        var result = Plan(TimeSpan.Zero, Agent("AGV-1", "N2", 180, "S", dueSeconds: 100), Agent("AGV-2", "E", 270, "W", dueSeconds: 100));

        Assert.Equal(["AGV-1", "AGV-2"], result.PassingOrder["M"]);
    }

    [Fact]
    public void 入れ替えても結果の並びは頼みの並び順のまま()
    {
        var result = Plan(Enough, Agent("AGV-1", "N2", 180, "S", dueSeconds: 100), Agent("AGV-2", "E", 270, "W", dueSeconds: 100));

        Assert.Equal(["AGV-1", "AGV-2"], result.Plans.Select(p => p.AgentId));
    }
}
