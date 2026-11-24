using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

/// <summary>
/// 行ける充電ポートがなくて待っている AGV を、充電ポートが空いたら向かわせることのテスト。
/// </summary>
/// <remarks>
/// 通常パネル A1 と A2 に、充電ピンのある待機パネル CP1（A1 の西）と CP2（A2 の東）がつながる。
/// </remarks>
public class DispatchingChargingPortTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly CarrierTypeId CarrierX = new("X");
    private static readonly PanelId A1 = new("A1");
    private static readonly PanelId A2 = new("A2");
    private static readonly PanelId CP1 = new("CP1");
    private static readonly PanelId CP2 = new("CP2");
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
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

    private static InMemoryMapStore Map()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [
                new Panel(A1, PanelType.Normal, false),
                new Panel(A2, PanelType.Normal, false),
                new Panel(CP1, PanelType.Standby, true),
                new Panel(CP2, PanelType.Standby, true),
            ],
            [
                new Connection(A1, A2, 800, new Heading(90), false),
                new Connection(A1, CP1, 800, new Heading(270), false),
                new Connection(A2, CP2, 800, new Heading(90), false),
            ]).Snapshot!);
        return store;
    }

    /// <summary>AGV-1 は A1、AGV-2 は A2 にいる。充電ポートは、どちらもほかの AGV が予約している。</summary>
    private static (DispatchingService Service, FakeFleetTraffic Traffic, RecordingPublisher Events) Setup()
    {
        var traffic = new FakeFleetTraffic();
        traffic.Set(Agv1, A1);
        traffic.Set(Agv2, A2);
        traffic.ReservedByOthers.UnionWith([CP1, CP2]);
        var events = new RecordingPublisher();
        var service = new DispatchingService(traffic, Map(), events, new FixedTimeProvider());
        service.RegisterAgv(Agv1, CarrierX, goChargeBelowPercent: 20, resumeAtPercent: 80);
        service.RegisterAgv(Agv2, CarrierX, goChargeBelowPercent: 20, resumeAtPercent: 80);
        return (service, traffic, events);
    }

    private static EventMetadata Meta() => EventMetadata.Start("test", new FixedTimeProvider());

    private static Task LowBatteryAsync(DispatchingService service, AgvId agv, int percent) =>
        service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), agv, percent), None);

    private static Task ReleasedAsync(DispatchingService service, FakeFleetTraffic traffic, PanelId port)
    {
        traffic.ReservedByOthers.Remove(port);
        return service.OnChargingPortReleasedAsync(new ChargingPortReleased(Meta(), port), None);
    }

    private static PanelId[] ChargingMovesOf(FakeFleetTraffic traffic, AgvId agv) =>
        traffic.Moves.Where(m => m.Agv == agv && m.CorrelationId == $"charge:{agv}").Select(m => m.To).ToArray();

    [Fact]
    public async Task 充電ポートが空いたら待っていたAGVを向かわせる()
    {
        var (service, traffic, events) = Setup();
        await LowBatteryAsync(service, Agv1, 10);
        var waiting = events.Published.OfType<NoChargingPortAvailable>().Count();

        await ReleasedAsync(service, traffic, CP1);

        Assert.Equal(1, waiting);
        Assert.Equal([CP1], ChargingMovesOf(traffic, Agv1));
    }

    [Fact]
    public async Task 待っているAGVが複数なら充電残量が少ないAGVから向かわせる()
    {
        var (service, traffic, _) = Setup();
        await LowBatteryAsync(service, Agv1, 15);
        await LowBatteryAsync(service, Agv2, 10);

        await ReleasedAsync(service, traffic, CP1);

        Assert.Equal([CP1], ChargingMovesOf(traffic, Agv2));
        Assert.Empty(ChargingMovesOf(traffic, Agv1)); // AGV-2 が向かっている CP1 は使っているので、待ったまま
    }

    [Fact]
    public async Task 充電残量が同じなら空いた充電ポートに近いAGVから向かわせる()
    {
        var (service, traffic, _) = Setup();
        traffic.Costs[(Agv1, CP1)] = TimeSpan.FromSeconds(8);
        traffic.Costs[(Agv2, CP1)] = TimeSpan.FromSeconds(3);
        await LowBatteryAsync(service, Agv1, 10);
        await LowBatteryAsync(service, Agv2, 10);

        await ReleasedAsync(service, traffic, CP1);

        Assert.Equal([CP1], ChargingMovesOf(traffic, Agv2));
        Assert.Empty(ChargingMovesOf(traffic, Agv1));
    }

    [Fact]
    public async Task ほかのAGVが向かっている充電ポートには向かわせない()
    {
        var (service, traffic, events) = Setup();
        traffic.ReservedByOthers.Remove(CP1);
        await LowBatteryAsync(service, Agv2, 10); // CP1 へ向かう（まだ予約していない）

        await LowBatteryAsync(service, Agv1, 10);

        Assert.Equal([CP1], ChargingMovesOf(traffic, Agv2));
        Assert.Empty(ChargingMovesOf(traffic, Agv1));
        Assert.Equal(Agv1, Assert.Single(events.Published.OfType<NoChargingPortAvailable>()).Agv);
    }

    [Fact]
    public async Task 停止中は充電ポートが空いても向かわせずスタートしたら向かわせる()
    {
        var (service, traffic, _) = Setup();
        await LowBatteryAsync(service, Agv1, 10);
        await service.StopAsync(None);

        await ReleasedAsync(service, traffic, CP1);
        var whileStopped = ChargingMovesOf(traffic, Agv1);
        await service.StartAsync(None);

        Assert.Empty(whileStopped);
        Assert.Equal([CP1], ChargingMovesOf(traffic, Agv1));
    }
}
