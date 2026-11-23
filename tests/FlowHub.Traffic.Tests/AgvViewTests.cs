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
    /// <param name="startType">N の種類。</param>
    private static async Task<TrafficService> ReadyAsync(PanelType startType = PanelType.Normal)
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            Panels.Select(id => new Panel(new PanelId(id), id == "N" ? startType : PanelType.Normal, false)),
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

    [Fact]
    public async Task 充電残量はまだ届いていなければわからず届いたら最後の値を持つ()
    {
        var service = await ReadyAsync();
        Assert.Null(service.GetAgv(Agv).BatteryPercent);

        await service.HandleBatteryChangedAsync(Agv, 57, None);
        await service.HandleBatteryChangedAsync(Agv, 55, None);

        Assert.Equal(55, service.GetAgv(Agv).BatteryPercent);
    }

    [Fact]
    public async Task キャリアを下げている途中かがわかる()
    {
        var service = await ReadyAsync(startType: PanelType.Transfer);
        Assert.False(service.GetAgv(Agv).CarrierMoving);

        await service.LowerCarrierAsync(Agv, "lower", None);

        var view = service.GetAgv(Agv);
        Assert.True(view.CarrierMoving);
        Assert.False(view.CarrierRaised);
    }

    [Fact]
    public async Task 登録したすべてのAGVの様子をIDの順に返す()
    {
        var service = await ReadyAsync();
        service.RegisterAgv(new AgvId("AGV-0"), new MotionProfile(500, 250, 90));

        var all = service.ListAgvs();

        Assert.Equal(["AGV-0", "AGV-1"], all.Select(a => a.Id.Value));
        Assert.Null(all[0].Position); // 位置がわからない
        Assert.Equal(AgvState.Unregistered, all[0].State);
        Assert.Equal(new Pose("N", 180), all[1].Position);
    }
}
