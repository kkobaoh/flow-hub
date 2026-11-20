using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

/// <summary>
/// 運転状態を停止にしたときに、通路にいる AGV を待機パネルへ動かして止めることのテスト。
/// </summary>
/// <remarks>
/// 通常パネル A1、A2 に、受け渡しパネル S（ステーション）と D（装置）、充電ピンのある待機パネル CP、充電ピンのない待機パネル SB1、SB2 がつながる。
/// 移動コストは、偽物の交通整理に決めておく（決めていなければ 10秒）。
/// </remarks>
public class DispatchingParkingTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly CarrierTypeId CarrierX = new("X");
    private static readonly PanelId A1 = new("A1");
    private static readonly PanelId A2 = new("A2");
    private static readonly PanelId S = new("S");
    private static readonly PanelId D = new("D");
    private static readonly PanelId CP = new("CP");
    private static readonly PanelId SB1 = new("SB1");
    private static readonly PanelId SB2 = new("SB2");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
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
                new Panel(S, PanelType.Transfer, false),
                new Panel(D, PanelType.Transfer, false),
                new Panel(CP, PanelType.Standby, true),
                new Panel(SB1, PanelType.Standby, false),
                new Panel(SB2, PanelType.Standby, false),
            ],
            [
                new Connection(A1, A2, 800, new Heading(90), false),
                new Connection(A1, S, 800, new Heading(0), false),
                new Connection(A1, D, 800, new Heading(180), false),
                new Connection(A1, CP, 800, new Heading(270), false),
                new Connection(A2, SB1, 800, new Heading(0), false),
                new Connection(A2, SB2, 800, new Heading(180), false),
            ]).Snapshot!);
        return store;
    }

    private static (DispatchingService Service, FakeFleetTraffic Traffic, RecordingPublisher Events) Setup(params AgvId[] agvs)
    {
        var traffic = new FakeFleetTraffic();
        var events = new RecordingPublisher();
        var service = new DispatchingService(traffic, Map(), events, new FixedTimeProvider());
        foreach (var agv in agvs)
        {
            traffic.Set(agv, A1);
            service.RegisterAgv(agv, CarrierX, goChargeBelowPercent: 20, resumeAtPercent: 80);
        }

        return (service, traffic, events);
    }

    private static EventMetadata Meta() => EventMetadata.Start("test", new FixedTimeProvider());

    private static PanelId[] ParkingOf(FakeFleetTraffic traffic, AgvId agv) =>
        traffic.Moves.Where(m => m.Agv == agv && m.CorrelationId == $"park:{agv}").Select(m => m.To).ToArray();

    [Fact]
    public async Task 停止したら通路にいるAGVを充電ピンのある待機パネルへ向かわせる()
    {
        var (service, traffic, _) = Setup(Agv1);
        traffic.Costs[(Agv1, SB1)] = TimeSpan.FromSeconds(1); // 充電ピンのない待機パネルのほうが近くても

        await service.StopAsync(None);

        Assert.Equal([CP], ParkingOf(traffic, Agv1));
    }

    [Fact]
    public async Task 充電ピンのある待機パネルが空いていなければ一番近い待機パネルへ向かわせる()
    {
        var (service, traffic, _) = Setup(Agv1);
        traffic.ReservedByOthers.Add(CP);
        traffic.Costs[(Agv1, SB1)] = TimeSpan.FromSeconds(20);
        traffic.Costs[(Agv1, SB2)] = TimeSpan.FromSeconds(15);
        traffic.Costs[(Agv1, S)] = TimeSpan.FromSeconds(1); // 受け渡しパネルのほうが近くても、待機パネルを先に選ぶ

        await service.StopAsync(None);

        Assert.Equal([SB2], ParkingOf(traffic, Agv1));
    }

    [Fact]
    public async Task 待機パネルが空いていなければ一番近い空いている受け渡しパネルへ向かわせる()
    {
        var (service, traffic, _) = Setup(Agv1);
        traffic.ReservedByOthers.UnionWith([CP, SB1, SB2, S]);

        await service.StopAsync(None);

        Assert.Equal([D], ParkingOf(traffic, Agv1));
    }

    [Fact]
    public async Task 待機パネルにも受け渡しパネルにも空きがなければその場で止めて知らせる()
    {
        var (service, traffic, events) = Setup(Agv1);
        traffic.ReservedByOthers.UnionWith([CP, SB1, SB2, S, D]);
        traffic.Set(Agv1, A1, stopsAt: A2); // 走っていて、止まれる最初の位置は A2

        await service.StopAsync(None);

        Assert.Equal([A2], ParkingOf(traffic, Agv1));
        Assert.Equal(Agv1, Assert.Single(events.Published.OfType<NoParkingSpace>()).Agv);
    }

    [Fact]
    public async Task ほかのAGVがいるパネルは空いていないとする()
    {
        var (service, traffic, _) = Setup(Agv1, Agv2);
        traffic.Set(Agv2, CP, control: ControlStatus.Locked);

        await service.StopAsync(None);

        Assert.DoesNotContain(CP, ParkingOf(traffic, Agv1));
        Assert.Empty(ParkingOf(traffic, Agv2));
    }

    [Fact]
    public async Task 止まれる最初の位置が待機パネルか受け渡しパネルならそのまま止める()
    {
        var (service, traffic, _) = Setup(Agv1, Agv2);
        traffic.Set(Agv1, SB1);
        traffic.Set(Agv2, A1, stopsAt: S); // S へ向かう区間を走っている

        await service.StopAsync(None);

        Assert.Empty(traffic.Moves);
    }

    [Theory]
    [InlineData(ControlStatus.Manual)]
    [InlineData(ControlStatus.Locked)]
    public async Task システムが動かさないAGVは待機パネルへ向かわせない(ControlStatus control)
    {
        var (service, traffic, _) = Setup(Agv1);
        traffic.Set(Agv1, A1, control: control);

        await service.StopAsync(None);

        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task 通路で緊急停止中のAGVは待機パネルへ向かわせずアラームクリアのあともその場で止めておく()
    {
        var (service, traffic, events) = Setup(Agv1);
        traffic.Agvs[Agv1] = traffic.Agvs[Agv1] with { State = AgvState.EmergencyStopped };

        await service.StopAsync(None);

        Assert.Equal([A1], ParkingOf(traffic, Agv1)); // 行き先を、止まれる最初の位置にしておく
        Assert.Empty(events.Published.OfType<NoParkingSpace>());
    }

    [Fact]
    public async Task その場で止めた緊急停止中のAGVの搬送はスタートしてアラームクリアしたら続きから進める()
    {
        var (service, traffic, _) = Setup(Agv1);
        var id = await service.RequestTransportAsync(
            new TransportOrder("test", ["C-1"], S, "station-1", D, "device-1", CarrierX, Now.AddMinutes(10)), None);
        await service.OnAgvStoppedAsync(new AgvStopped(Meta(), Agv1), None);
        traffic.Agvs[Agv1] = traffic.Agvs[Agv1] with { State = AgvState.EmergencyStopped };
        await service.StopAsync(None);

        await service.StartAsync(None);
        var afterStart = traffic.Moves[^1];
        traffic.Agvs[Agv1] = traffic.Agvs[Agv1] with { State = AgvState.Idle };
        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv1), None);

        Assert.Equal((Agv1, A1, $"park:{Agv1}"), afterStart); // 緊急停止中は、まだ向かわせない
        Assert.Equal((Agv1, S, $"transport:{id}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 割り当て停止のAGVも待機パネルへ向かわせる()
    {
        var (service, traffic, _) = Setup(Agv1);
        traffic.Set(Agv1, A1, control: ControlStatus.AssignmentPaused);

        await service.StopAsync(None);

        Assert.Equal([CP], ParkingOf(traffic, Agv1));
    }

    [Fact]
    public async Task 充電残量が少ないAGVから充電ピンのある待機パネルを決める()
    {
        var (service, traffic, _) = Setup(Agv1, Agv2);
        traffic.Set(Agv2, A2);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv1, 50), None);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv2, 30), None);

        await service.StopAsync(None);

        Assert.Equal([CP], ParkingOf(traffic, Agv2));
        Assert.NotEqual([CP], ParkingOf(traffic, Agv1));
    }

    [Fact]
    public async Task 充電残量がわからないAGVは最後に決める()
    {
        var (service, traffic, _) = Setup(Agv1, Agv2);
        traffic.Set(Agv2, A2);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv2, 90), None);

        await service.StopAsync(None);

        Assert.Equal([CP], ParkingOf(traffic, Agv2));
    }

    [Fact]
    public async Task 充電残量が同じなら空いている候補に近いAGVから決める()
    {
        var (service, traffic, _) = Setup(Agv1, Agv2);
        traffic.Set(Agv2, A2);
        traffic.Costs[(Agv1, CP)] = TimeSpan.FromSeconds(8);
        traffic.Costs[(Agv2, CP)] = TimeSpan.FromSeconds(3);

        await service.StopAsync(None);

        Assert.Equal([CP], ParkingOf(traffic, Agv2));
        Assert.NotEqual([CP], ParkingOf(traffic, Agv1)); // 先に決めた AGV の行き先は空いていない
    }

    [Fact]
    public async Task 充電ポートへ向かっているAGVはそのまま向かわせる()
    {
        var (service, traffic, _) = Setup(Agv1);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv1, 10), None); // CP へ向かう
        traffic.Set(Agv1, A1, stopsAt: A1);
        traffic.Moves.Clear();

        await service.StopAsync(None);

        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task 停止中にあとからシステムが動かすようになったAGVは向かわせない()
    {
        var (service, traffic, _) = Setup(Agv1);
        traffic.Set(Agv1, A1, control: ControlStatus.Manual);
        await service.StopAsync(None);

        await service.ChangeControlStatusAsync(Agv1, ControlStatus.Auto, position: null, None);

        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task スタートしたら待機パネルに止めた搬送を続きから進める()
    {
        var (service, traffic, _) = Setup(Agv1);
        var id = await service.RequestTransportAsync(
            new TransportOrder("test", ["C-1"], S, "station-1", D, "device-1", CarrierX, Now.AddMinutes(10)), None);
        await service.StopAsync(None);
        var parking = ParkingOf(traffic, Agv1);

        traffic.Set(Agv1, CP);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv1, CP), None);
        await service.StartAsync(None);

        Assert.Equal([CP], parking);
        Assert.Equal((Agv1, S, $"transport:{id}"), traffic.Moves[^1]); // もう一度、搬送元へ向かう
    }
}
