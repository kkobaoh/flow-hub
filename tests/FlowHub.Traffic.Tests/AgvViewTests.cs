using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// AGV の様子（ほかのコンテキストから見えるもの）のテスト。
/// </summary>
public class AgvViewTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] Panels = ["N", "M", "S"];

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    /// <summary>N →(180度) M →(180度) S の通路。AGV は N で南（180度）を向いて待機中。</summary>
    private static async Task<TrafficService> ReadyAsync()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            Panels.Select(id => new Panel(new PanelId(id), PanelType.Normal, false)),
            [
                new Connection(new PanelId("N"), new PanelId("M"), 800, new Heading(180), OneWay: false),
                new Connection(new PanelId("M"), new PanelId("S"), 800, new Heading(180), OneWay: false),
            ]).Snapshot!);
        var service = new TrafficService(store, new IgnoringPublisher(), new FixedTimeProvider());
        service.RegisterAgv(Agv, new MotionProfile(500, 250, 90));
        await service.ResetPositionAsync(Agv, new Pose("N", 180), None);
        await service.HandleCommandRequestAsync(Agv, -1, None);
        await service.HandlePositionResetCompletedAsync(Agv, 1, None);
        return service;
    }

    [Fact]
    public async Task 止まっているAGVの止まれる最初の位置は今の位置()
    {
        var service = await ReadyAsync();

        Assert.Equal(new Pose("N", 180), service.GetAgv(Agv).StopsAt);
    }

    [Fact]
    public async Task 走っているAGVの止まれる最初の位置は渡した区間を走り終えたところ()
    {
        var service = await ReadyAsync();
        await service.MoveAgvAsync(Agv, new PanelId("S"), null, "move", None); // N → M → S を1つの区間で渡す

        var view = service.GetAgv(Agv);

        Assert.Equal(new Pose("N", 180), view.Position);
        Assert.Equal(new Pose("S", 180), view.StopsAt);
    }
}
