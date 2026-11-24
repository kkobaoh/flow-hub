using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class RouteGraphFactoryTests
{
    private static Panel Panel(string id, PanelType type) => new(new PanelId(id), type, HasChargingPin: false);

    private static MapSnapshot Map(IEnumerable<Panel> panels, IEnumerable<Connection> connections) =>
        MapSnapshot.Create(panels, connections).Snapshot!;

    private static readonly RoutePlanner Planner = new();

    private static readonly MotionProfile Motion = new(500, 250, 90);

    [Fact]
    public void 通常パネルは回転できて出入りの制限がない()
    {
        var map = Map(
            [Panel("A", PanelType.Normal), Panel("B", PanelType.Normal)],
            [new Connection(new PanelId("A"), new PanelId("B"), 800, new Heading(0), OneWay: false)]);

        var graph = RouteGraphFactory.FromMap(map);

        // A で 180度を向いていても、回転して前進するか、後退して B へ行ける
        var result = Planner.Plan(new RouteRequest(graph, new Pose("A", 180), "B", null, Motion));
        Assert.True(result.Found);
    }

    [Theory]
    [InlineData(PanelType.Transfer)]
    [InlineData(PanelType.Standby)]
    public void 受け渡しパネルと待機パネルは前進で入り後退で出て中では回転しない(PanelType type)
    {
        var map = Map(
            [Panel("A", PanelType.Normal), Panel("D", type)],
            [new Connection(new PanelId("A"), new PanelId("D"), 800, new Heading(0), OneWay: false)]);

        var graph = RouteGraphFactory.FromMap(map);

        // A で 180度を向いていると、後退で D に入るほうが速いが、前進でしか入れない
        var enter = Planner.Plan(new RouteRequest(graph, new Pose("A", 180), "D", null, Motion));
        Assert.Equal(
            [
                new RotateStep("A", 0, new Pose("A", 0)),
                new MoveStep(MoveDirection.Forward, "A", "D", 800, new Pose("D", 0)),
            ],
            enter.Steps);

        // D の中では回転できない
        var rotate = Planner.Plan(new RouteRequest(graph, new Pose("D", 0), "D", 90, Motion));
        Assert.False(rotate.Found);
    }

    [Fact]
    public void つながりの距離と向きと一方通行がそのまま辺になる()
    {
        // A →(45度、一方通行) B ⇄ C ⇄ A（どこへでも行けるように、帰り道を C 経由で用意する）
        var map = Map(
            [Panel("A", PanelType.Normal), Panel("B", PanelType.Normal), Panel("C", PanelType.Normal)],
            [
                new Connection(new PanelId("A"), new PanelId("B"), 1200, new Heading(45), OneWay: true),
                new Connection(new PanelId("B"), new PanelId("C"), 800, new Heading(180), OneWay: false),
                new Connection(new PanelId("C"), new PanelId("A"), 800, new Heading(270), OneWay: false),
            ]);

        var graph = RouteGraphFactory.FromMap(map);

        var forward = Planner.Plan(new RouteRequest(graph, new Pose("A", 45), "B", null, Motion));
        Assert.Equal([new MoveStep(MoveDirection.Forward, "A", "B", 1200, new Pose("B", 45))], forward.Steps);

        // B から A へは、一方通行を逆に通らず、C を回る
        var back = Planner.Plan(new RouteRequest(graph, new Pose("B", 45), "A", null, Motion));
        Assert.DoesNotContain(back.Steps, step => step is MoveStep { From: "B", To: "A" });
        Assert.Contains(back.Steps, step => step is MoveStep { To: "C" });
    }
}
