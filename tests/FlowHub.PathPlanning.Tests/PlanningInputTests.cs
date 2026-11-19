namespace FlowHub.PathPlanning.Tests;

/// <summary>
/// 全台の経路の計画の入力（マップ、AGV、障害物）の決まりのテスト。
/// </summary>
public class PlanningInputTests
{
    private static readonly AgentMotion Motion = new(ForwardSpeedMmPerSecond: 500, BackwardSpeedMmPerSecond: 250, RotationSpeedDegreesPerSecond: 90);

    /// <summary>A →(0度) B →(0度) T（行き止まり）。</summary>
    private static PlanningMap Map() =>
        new(
            [
                new PlanningNode("A", CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false),
                new PlanningNode("B", CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false),
                new PlanningNode("T", CanRotate: false, ForwardEntryOnly: true, BackwardExitOnly: true),
            ],
            [
                new PlanningEdge("A", "B", DistanceMm: 800, Heading: 0, OneWay: false),
                new PlanningEdge("B", "T", DistanceMm: 800, Heading: 0, OneWay: false),
            ]);

    private static PlanningAgent Agent(string id, string node, string? goal = null) =>
        new(id, new AgentPose(node, 0), Motion, goal is null ? null : new AgentGoal(goal, Heading: null), Committed: [], Movable: true);

    // ---- マップ ----

    [Fact]
    public void ノードとつながりを持つ()
    {
        var map = Map();

        Assert.Equal(["A", "B", "T"], map.Nodes.Select(n => n.Id));
        Assert.Equal(2, map.Edges.Count);
        Assert.True(map.Contains("T"));
        Assert.False(map.Contains("X"));
    }

    [Fact]
    public void ノードが重複していたらエラーにする()
    {
        Assert.Throws<ArgumentException>(() => new PlanningMap(
            [new PlanningNode("A", true, false, false), new PlanningNode("A", true, false, false)],
            []));
    }

    [Fact]
    public void ないノードを指すつながりはエラーにする()
    {
        Assert.Throws<ArgumentException>(() => new PlanningMap(
            [new PlanningNode("A", true, false, false)],
            [new PlanningEdge("A", "X", 800, 0, false)]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(800, -1)]
    [InlineData(800, 360)]
    public void つながりの距離は1以上で向きは0から359(int distance, int heading)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanningEdge("A", "B", distance, heading, false));
    }

    // ---- AGV ----

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    public void 姿勢の向きは0から359(int heading)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentPose("A", heading));
    }

    [Theory]
    [InlineData(0, 250, 90)]
    [InlineData(500, 0, 90)]
    [InlineData(500, 250, 0)]
    public void 速さは0より大きい(double forward, double backward, double rotation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentMotion(forward, backward, rotation));
    }

    [Fact]
    public void ゴールの向きは指定しないこともできて指定するなら0から359()
    {
        Assert.Null(new AgentGoal("T", Heading: null).Heading);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentGoal("T", Heading: 360));
    }

    // ---- 計画の頼み ----

    [Fact]
    public void AGVの並び順が計画の順番になる()
    {
        var request = new PlanningRequest(Map(), [Agent("AGV-2", "A", "T"), Agent("AGV-1", "B")], Obstacles: [], ImprovementBudget: TimeSpan.FromMilliseconds(200), Horizon: TimeSpan.FromMinutes(10));

        Assert.Equal(["AGV-2", "AGV-1"], request.Agents.Select(a => a.Id));
    }

    [Fact]
    public void 同じAGVが二度出てきたらエラーにする()
    {
        Assert.Throws<ArgumentException>(() =>
            new PlanningRequest(Map(), [Agent("AGV-1", "A"), Agent("AGV-1", "B")], Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void AGVの位置やゴールや障害物がマップにないノードならエラーにする()
    {
        Assert.Throws<ArgumentException>(() =>
            new PlanningRequest(Map(), [Agent("AGV-1", "X")], Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));
        Assert.Throws<ArgumentException>(() =>
            new PlanningRequest(Map(), [Agent("AGV-1", "A", goal: "X")], Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));
        Assert.Throws<ArgumentException>(() =>
            new PlanningRequest(Map(), [], Obstacles: [new PlanningObstacle("AGV-9", ["X"])], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void 改善にかける時間は0以上で計画する時間の長さの上限は0より大きい()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanningRequest(Map(), [], Obstacles: [], ImprovementBudget: TimeSpan.FromMilliseconds(-1), Horizon: TimeSpan.FromMinutes(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanningRequest(Map(), [], Obstacles: [], ImprovementBudget: TimeSpan.Zero, Horizon: TimeSpan.Zero));
    }

    [Fact]
    public void 障害物は1つ以上のノードを塞ぐ()
    {
        Assert.Throws<ArgumentException>(() => new PlanningObstacle("AGV-9", []));
    }
}
