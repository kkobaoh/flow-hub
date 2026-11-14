namespace FlowHub.Routing.Tests;

public class RoutePlannerTests
{
    // 前進 500mm/s、後退 250mm/s、回転 90度/s
    private static readonly MotionProfile Motion = new(ForwardSpeedMmPerSecond: 500, BackwardSpeedMmPerSecond: 250, RotationSpeedDegreesPerSecond: 90);

    private static readonly RoutePlanner Planner = new();

    private static RouteNode Normal(string id) => new(id, CanRotate: true, ForwardEntryOnly: false, BackwardExitOnly: false);

    /// <summary>受け渡しパネルや待機パネルのような行き止まり。</summary>
    private static RouteNode DeadEnd(string id) => new(id, CanRotate: false, ForwardEntryOnly: true, BackwardExitOnly: true);

    private static RouteEdge Edge(string from, string to, int heading, int distanceMm = 800, bool oneWay = false) =>
        new(from, to, distanceMm, heading, oneWay);

    private static RouteResult Plan(
        IEnumerable<RouteNode> nodes,
        IEnumerable<RouteEdge> edges,
        Pose start,
        string goal,
        int? goalHeading = null,
        MotionProfile? motion = null) =>
        Planner.Plan(new RouteRequest(new RouteGraph(nodes, edges), start, goal, goalHeading, motion ?? Motion));

    [Fact]
    public void まっすぐ前進してゴールに着く()
    {
        // A → B → C（すべて 0度の向き）
        var result = Plan(
            [Normal("A"), Normal("B"), Normal("C")],
            [Edge("A", "B", heading: 0), Edge("B", "C", heading: 0)],
            new Pose("A", 0),
            "C");

        Assert.True(result.Found);
        Assert.Equal(
            [
                new MoveStep(MoveDirection.Forward, "A", "B", 800, new Pose("B", 0)),
                new MoveStep(MoveDirection.Forward, "B", "C", 800, new Pose("C", 0)),
            ],
            result.Steps);
        Assert.Equal(1600.0 / 500, result.Cost.TotalSeconds, precision: 6);
    }

    [Fact]
    public void 曲がるところではその場で回転する()
    {
        // A →(0度) B →(90度) C
        var result = Plan(
            [Normal("A"), Normal("B"), Normal("C")],
            [Edge("A", "B", heading: 0), Edge("B", "C", heading: 90)],
            new Pose("A", 0),
            "C");

        Assert.Equal(
            [
                new MoveStep(MoveDirection.Forward, "A", "B", 800, new Pose("B", 0)),
                new RotateStep("B", 90, new Pose("B", 90)),
                new MoveStep(MoveDirection.Forward, "B", "C", 800, new Pose("C", 90)),
            ],
            result.Steps);
        Assert.Equal((1600.0 / 500) + (90.0 / 90), result.Cost.TotalSeconds, precision: 6);
    }

    [Fact]
    public void 回転は近い回りの角度でコストを数える()
    {
        // 0度から 270度へは、左に 90度回る
        var result = Plan(
            [Normal("A"), Normal("B")],
            [Edge("A", "B", heading: 270)],
            new Pose("A", 0),
            "B");

        Assert.Equal(new RotateStep("A", 270, new Pose("A", 270)), result.Steps[0]);
        Assert.Equal((90.0 / 90) + (800.0 / 500), result.Cost.TotalSeconds, precision: 6);
    }

    [Fact]
    public void 斜めのラインにも合わせて回転できる()
    {
        var result = Plan(
            [Normal("A"), Normal("B")],
            [Edge("A", "B", heading: 45)],
            new Pose("A", 0),
            "B");

        Assert.Equal(new RotateStep("A", 45, new Pose("A", 45)), result.Steps[0]);
    }

    [Fact]
    public void 後退のほうが速ければ後退で戻る()
    {
        // A →(0度) B。B で 0度を向いたまま A へ戻る。
        // 後退：800 / 250 = 3.2秒、回転して前進：180 / 90 + 800 / 500 = 3.6秒
        var result = Plan(
            [Normal("A"), Normal("B")],
            [Edge("A", "B", heading: 0)],
            new Pose("B", 0),
            "A");

        Assert.Equal([new MoveStep(MoveDirection.Backward, "B", "A", 800, new Pose("A", 0))], result.Steps);
        Assert.Equal(3.2, result.Cost.TotalSeconds, precision: 6);
    }

    [Fact]
    public void 回転して前進するほうが速ければ回転する()
    {
        // 回転 180度/s なら、回転して前進：1 + 1.6 = 2.6秒 < 後退 3.2秒
        var fastRotation = Motion with { RotationSpeedDegreesPerSecond = 180 };

        var result = Plan(
            [Normal("A"), Normal("B")],
            [Edge("A", "B", heading: 0)],
            new Pose("B", 0),
            "A",
            motion: fastRotation);

        Assert.Equal(
            [
                new RotateStep("B", 180, new Pose("B", 180)),
                new MoveStep(MoveDirection.Forward, "B", "A", 800, new Pose("A", 180)),
            ],
            result.Steps);
    }

    [Fact]
    public void 行き止まりには前進でしか入れない()
    {
        // A →(0度) T（行き止まり）。A で 180度を向いている。
        // 後退で入るほうが速いが、行き止まりには前進でしか入れない。
        var result = Plan(
            [Normal("A"), DeadEnd("T")],
            [Edge("A", "T", heading: 0)],
            new Pose("A", 180),
            "T");

        Assert.Equal(
            [
                new RotateStep("A", 0, new Pose("A", 0)),
                new MoveStep(MoveDirection.Forward, "A", "T", 800, new Pose("T", 0)),
            ],
            result.Steps);
    }

    [Fact]
    public void 行き止まりからは後退でしか出られず中では回転しない()
    {
        // B ←(180度) A →(0度) T（行き止まり）。T で 0度を向いている。
        var result = Plan(
            [Normal("A"), Normal("B"), DeadEnd("T")],
            [Edge("A", "T", heading: 0), Edge("A", "B", heading: 180)],
            new Pose("T", 0),
            "B");

        Assert.Equal(new MoveStep(MoveDirection.Backward, "T", "A", 800, new Pose("A", 0)), result.Steps[0]);
        Assert.DoesNotContain(result.Steps, step => step is RotateStep { NodeId: "T" });
    }

    [Fact]
    public void 一方通行は逆向きに通れない()
    {
        var result = Plan(
            [Normal("A"), Normal("B")],
            [Edge("A", "B", heading: 0, oneWay: true)],
            new Pose("B", 0),
            "A");

        Assert.False(result.Found);
        Assert.Equal(RouteFailure.NoRoute, result.Failure);
    }

    [Fact]
    public void 一方通行は後退でも逆向きには通れない()
    {
        // B で 0度を向いたまま後退すると、A へ（180度の方向へ）進むことになる
        var result = Plan(
            [Normal("A"), Normal("B"), Normal("C")],
            [Edge("A", "B", heading: 0, oneWay: true), Edge("B", "C", heading: 90), Edge("C", "A", heading: 225)],
            new Pose("B", 0),
            "A");

        Assert.True(result.Found);
        Assert.DoesNotContain(result.Steps, step => step is MoveStep { From: "B", To: "A" });
    }

    [Fact]
    public void ゴールの向きを指定するとゴールで向きを合わせる()
    {
        var result = Plan(
            [Normal("A"), Normal("B")],
            [Edge("A", "B", heading: 0)],
            new Pose("A", 0),
            "B",
            goalHeading: 90);

        Assert.Equal(new RotateStep("B", 90, new Pose("B", 90)), result.Steps[^1]);
    }

    [Fact]
    public void 行き止まりの中では向きを変えられないので届かない向きは経路なし()
    {
        var result = Plan(
            [Normal("A"), DeadEnd("T")],
            [Edge("A", "T", heading: 0)],
            new Pose("A", 0),
            "T",
            goalHeading: 180);

        Assert.Equal(RouteFailure.NoRoute, result.Failure);
    }

    [Fact]
    public void つながっていないパネルへは経路なし()
    {
        var result = Plan([Normal("A"), Normal("B")], [], new Pose("A", 0), "B");

        Assert.False(result.Found);
        Assert.Empty(result.Steps);
        Assert.Equal(RouteFailure.NoRoute, result.Failure);
    }

    [Theory]
    [InlineData("X", "A")]
    [InlineData("A", "X")]
    public void 存在しないパネルを指定すると失敗する(string start, string goal)
    {
        var result = Plan([Normal("A")], [], new Pose(start, 0), goal);

        Assert.Equal(RouteFailure.UnknownNode, result.Failure);
    }

    [Fact]
    public void 出発地がゴールなら何もしない()
    {
        var result = Plan([Normal("A")], [], new Pose("A", 0), "A");

        Assert.True(result.Found);
        Assert.Empty(result.Steps);
        Assert.Equal(TimeSpan.Zero, result.Cost);
    }

    [Fact]
    public void 一番コストの小さい経路を選ぶ()
    {
        // A から D へ：A→B→D（短い）と A→C→D（長い）
        var result = Plan(
            [Normal("A"), Normal("B"), Normal("C"), Normal("D")],
            [
                Edge("A", "B", heading: 0, distanceMm: 800),
                Edge("B", "D", heading: 0, distanceMm: 800),
                Edge("A", "C", heading: 0, distanceMm: 2000),
                Edge("C", "D", heading: 0, distanceMm: 2000),
            ],
            new Pose("A", 0),
            "D");

        Assert.Equal(["B", "D"], result.Steps.OfType<MoveStep>().Select(s => s.To));
    }
}
