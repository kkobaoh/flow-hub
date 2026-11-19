using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 交通整理が全台の経路を計画し、パネルごとの通る順番を守らせるテスト。
/// </summary>
public class TrafficPlanningTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly DateTimeOffset Early = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = Early.AddMinutes(10);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] CrossroadsPanels = ["N", "M", "S", "E", "W"];

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Early;
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<IIntegrationEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 十字路。N →(180度) M →(180度) S と、E →(270度) M →(270度) W が、M で交わる。
    /// </summary>
    private static InMemoryMapStore Crossroads()
    {
        var store = new InMemoryMapStore();
        PanelId P(string id) => new(id);
        store.Publish(MapSnapshot.Create(
            CrossroadsPanels.Select(id => new Panel(P(id), PanelType.Normal, false)),
            [
                new Connection(P("N"), P("M"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("S"), 800, new Heading(180), OneWay: false),
                new Connection(P("E"), P("M"), 800, new Heading(270), OneWay: false),
                new Connection(P("M"), P("W"), 800, new Heading(270), OneWay: false),
            ]).Snapshot!);
        return store;
    }

    /// <summary>
    /// 2台を登録し、位置リセットを済ませて待機中にする。AGV-1 は N（180度）、AGV-2 は E（270度）。
    /// </summary>
    private static async Task<(TrafficService Service, RecordingPublisher Events)> TwoAgvsAsync()
    {
        var events = new RecordingPublisher();
        var service = new TrafficService(Crossroads(), events, new FixedTimeProvider());
        foreach (var (agv, pose) in new[] { (Agv1, new Pose("N", 180)), (Agv2, new Pose("E", 270)) })
        {
            service.RegisterAgv(agv, Motion);
            await service.ResetPositionAsync(agv, pose, None);
            await service.HandleCommandRequestAsync(agv, -1, None);
            await service.HandlePositionResetCompletedAsync(agv, 1, None);
        }

        events.Published.Clear();
        return (service, events);
    }

    private static async Task<MotionCommand?> MotionOf(TrafficService service, AgvId agv) =>
        (await service.HandleCommandRequestAsync(agv, 1, None)).OfType<MotionCommand>().SingleOrDefault();

    [Fact]
    public async Task 先のAGVが十字路を通り過ぎるまであとのAGVには区間を渡さず通り過ぎたら渡す()
    {
        var (service, _) = await TwoAgvsAsync();
        await service.MoveAgvAsync(Agv1, new PanelId("S"), Early, "transport-1", None);

        var result = await service.MoveAgvAsync(Agv2, new PanelId("W"), Late, "transport-2", None);
        var beforePassing = await MotionOf(service, Agv2);
        await service.HandleMoveCompletedAsync(Agv1, 2, MoveDirection.Forward, new PanelId("M"), None);
        await service.HandleMoveCompletedAsync(Agv1, 2, MoveDirection.Forward, new PanelId("S"), None);
        var afterPassing = await MotionOf(service, Agv2);

        Assert.Equal(MoveResult.Started, result); // 計画どおりの順番待ちは、待っているとはしない
        Assert.Null(beforePassing);
        Assert.Equal<TravelCommand>(
            [new ForwardCommand(800, new PanelId("M")), new ForwardCommand(800, new PanelId("W"))],
            afterPassing!.Steps);
    }

    [Fact]
    public async Task 必要時刻が早いAGVを先に通す()
    {
        // AGV-2 は緊急停止中なので、計画はあっても区間を渡せない。そこへ、必要時刻の早い AGV-1 の頼みが来る
        var (service, _) = await TwoAgvsAsync();
        await service.EmergencyStopAsync(Agv2, None);
        await service.MoveAgvAsync(Agv2, new PanelId("W"), Late, "transport-2", None);

        await service.MoveAgvAsync(Agv1, new PanelId("S"), Early, "transport-1", None);
        var agv1 = await MotionOf(service, Agv1);

        Assert.NotNull(agv1); // AGV-1 が先に十字路を通る
    }

    [Fact]
    public async Task 必要時刻が同じなら頼まれた順に通す()
    {
        var (service, _) = await TwoAgvsAsync();
        await service.EmergencyStopAsync(Agv1, None);
        await service.MoveAgvAsync(Agv1, new PanelId("S"), Early, "transport-1", None); // 先に頼まれた

        await service.MoveAgvAsync(Agv2, new PanelId("W"), Early, "transport-2", None);

        Assert.Null(await MotionOf(service, Agv2)); // AGV-1 が先なので、AGV-2 は待つ
    }

    [Fact]
    public async Task 動かせないAGVに塞がれていたら待ち位置リセットで空いたら計画を立て直して出発する()
    {
        // AGV-2 が十字路の真ん中 M にいて、どかせない（行き先がない）
        var (service, _) = await TwoAgvsAsync();
        await service.ResetPositionAsync(Agv2, new Pose("M", 270), None);
        await service.HandleCommandRequestAsync(Agv2, 1, None);
        await service.HandlePositionResetCompletedAsync(Agv2, 2, None);

        var result = await service.MoveAgvAsync(Agv1, new PanelId("S"), Early, "transport-1", None);
        var whileBlocked = await MotionOf(service, Agv1);
        await service.ResetPositionAsync(Agv2, new Pose("W", 270), None);
        await service.HandleCommandRequestAsync(Agv2, 2, None);
        await service.HandlePositionResetCompletedAsync(Agv2, 3, None);
        var afterReset = await MotionOf(service, Agv1);

        Assert.Equal(MoveResult.Waiting, result);
        Assert.Null(whileBlocked);
        Assert.NotNull(afterReset);
    }

    [Fact]
    public async Task マップの上に経路がなければ経路が見つからないとする()
    {
        var events = new RecordingPublisher();
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [new Panel(new PanelId("A"), PanelType.Normal, false), new Panel(new PanelId("X"), PanelType.Normal, false)],
            []).Snapshot!);
        var service = new TrafficService(store, events, new FixedTimeProvider());
        service.RegisterAgv(Agv1, Motion);
        await service.ResetPositionAsync(Agv1, new Pose("A", 0), None);
        await service.HandleCommandRequestAsync(Agv1, -1, None);
        await service.HandlePositionResetCompletedAsync(Agv1, 1, None);

        var result = await service.MoveAgvAsync(Agv1, new PanelId("X"), null, "transport-1", None);

        Assert.Equal(MoveResult.NoRoute, result);
    }
}
