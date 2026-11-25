using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 新しいマップの版を公開するときに、いるパネルが新しい版にない AGV を切り離しにするテスト。
/// </summary>
public class MapPublishTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] LinePanels = ["A", "B", "C"];

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    /// <summary>A ─(90度) B ─(90度) C。</summary>
    private static InMemoryMapStore Line()
    {
        var store = new InMemoryMapStore();
        static PanelId P(string id) => new(id);
        store.Publish(MapSnapshot.Create(
            LinePanels.Select(id => new Panel(P(id), PanelType.Normal, false)),
            [
                new Connection(P("A"), P("B"), 800, new Heading(90), OneWay: false),
                new Connection(P("B"), P("C"), 800, new Heading(90), OneWay: false),
            ]).Snapshot!);
        return store;
    }

    private static async Task ReadyAsync(TrafficService service, AgvId agv, Pose pose)
    {
        service.RegisterAgv(agv, Motion);
        await service.ResetPositionAsync(agv, pose, None);
        await service.HandleCommandRequestAsync(agv, -1, None);
        await service.HandlePositionResetCompletedAsync(agv, 1, None);
    }

    /// <summary>AGV-1 は A、AGV-2 は C で待機中。</summary>
    private static async Task<TrafficService> TwoAgvsAsync()
    {
        var service = new TrafficService(Line(), new IgnoringPublisher(), TimeProvider.System);
        await ReadyAsync(service, Agv1, new Pose("A", 90));
        await ReadyAsync(service, Agv2, new Pose("C", 90));
        return service;
    }

    [Fact]
    public async Task いるパネルが新しい版にないAGVだけを切り離しにし位置をわからなくして予約を外す()
    {
        var service = await TwoAgvsAsync();

        var detached = await service.DetachAgvsOffMapAsync(new HashSet<PanelId> { new("A"), new("B") }, None);

        Assert.Equal([Agv2], detached);
        var agv2 = service.GetAgv(Agv2);
        Assert.Equal(ControlStatus.Detached, agv2.Control);
        Assert.Null(agv2.Position);
        Assert.Equal(AgvState.Unregistered, agv2.State);
        Assert.False(service.IsReservedByOther(new PanelId("C"), Agv1)); // C の予約は外れた
        Assert.Equal(ControlStatus.Auto, service.GetAgv(Agv1).Control);
        Assert.Equal(new Pose("A", 90), service.GetAgv(Agv1).Position);
    }

    [Fact]
    public async Task 切り離しにしたAGVは位置を指定して戻せる()
    {
        var service = await TwoAgvsAsync();
        await service.DetachAgvsOffMapAsync(new HashSet<PanelId> { new("A"), new("B") }, None);

        var accepted = await service.ChangeControlStatusAsync(Agv2, ControlStatus.Auto, new Pose("B", 90), None);

        Assert.True(accepted);
        Assert.Equal(AgvState.ResettingPosition, service.GetAgv(Agv2).State);
    }

    [Fact]
    public async Task いるパネルがどれも新しい版にあれば何もしない()
    {
        var service = await TwoAgvsAsync();

        var detached = await service.DetachAgvsOffMapAsync(new HashSet<PanelId> { new("A"), new("B"), new("C") }, None);

        Assert.Empty(detached);
        Assert.Equal(ControlStatus.Auto, service.GetAgv(Agv2).Control);
    }
}
