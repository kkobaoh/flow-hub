using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

public class DispatchingServiceTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly CarrierTypeId CarrierX = new("X");
    private static readonly PanelId A = new("A");
    private static readonly PanelId S = new("S");
    private static readonly PanelId D = new("D");
    private static readonly PanelId CP = new("CP");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
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

        public IEnumerable<T> Of<T>() => Published.OfType<T>();
    }

    /// <summary>交通整理の偽物。頼まれたことを記録する。</summary>
    private sealed class FakeTraffic : ITrafficService
    {
        public Dictionary<AgvId, AgvView> Agvs { get; } = [];

        public HashSet<PanelId> ReservedByOthers { get; } = [];

        public MoveResult NextMoveResult { get; set; } = MoveResult.Started;

        public List<(AgvId Agv, PanelId To, string CorrelationId)> Moves { get; } = [];

        public List<AgvId> Lowers { get; } = [];

        public List<AgvId> Raises { get; } = [];

        public AgvView GetAgv(AgvId id) => Agvs[id];

        public TimeSpan? EstimateMoveCost(AgvId id, PanelId destination) => TimeSpan.FromSeconds(10);

        public bool IsReservedByOther(PanelId panel, AgvId self) => ReservedByOthers.Contains(panel);

        public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, string correlationId, CancellationToken cancellationToken)
        {
            Moves.Add((id, destination, correlationId));
            return Task.FromResult(NextMoveResult);
        }

        public Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken)
        {
            Lowers.Add(id);
            return Task.CompletedTask;
        }

        public Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken)
        {
            Raises.Add(id);
            return Task.CompletedTask;
        }

        public void SetAgv(PanelId at, AgvState state = AgvState.Idle, bool carrierRaised = true) =>
            Agvs[Agv] = new AgvView(Agv, new Pose(at.Value, 0), state, carrierRaised, CumulativeDistanceMm: 0);
    }

    /// <summary>
    /// A（通常）につながる、S（ステーションの受け渡し）、D（装置の受け渡し）、CP（充電ピンのある待機パネル）。
    /// </summary>
    private static InMemoryMapStore MapStore()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [
                new Panel(A, PanelType.Normal, false),
                new Panel(S, PanelType.Transfer, false),
                new Panel(D, PanelType.Transfer, false),
                new Panel(CP, PanelType.Standby, true),
            ],
            [
                new Connection(A, S, 800, new Heading(0), false),
                new Connection(A, D, 800, new Heading(90), false),
                new Connection(A, CP, 800, new Heading(180), false),
            ]).Snapshot!);
        return store;
    }

    private static TransportOrder Order(int neededInMinutes = 10) => new(
        Requester: "ToolPlanning",
        CargoRefs: ["C-1"],
        SourcePanel: S,
        SourceEndpoint: "station-1",
        DestinationPanel: D,
        DestinationEndpoint: "device-1",
        Carrier: CarrierX,
        NeededBy: Now.AddMinutes(neededInMinutes));

    private static EventMetadata Meta() => EventMetadata.Start("test", new FixedTimeProvider());

    private static (DispatchingService Service, FakeTraffic Traffic, RecordingPublisher Events) Setup(PanelId agvAt)
    {
        var traffic = new FakeTraffic();
        traffic.SetAgv(agvAt);
        var events = new RecordingPublisher();
        var service = new DispatchingService(traffic, MapStore(), events, new FixedTimeProvider());
        service.RegisterAgv(Agv, CarrierX, goChargeBelowPercent: 20, resumeAtPercent: 80);
        return (service, traffic, events);
    }

    /// <summary>搬送を頼み、搬送元の受け渡しを終えてキャリアを上げるところまで進める。</summary>
    private static async Task<TransportId> LoadedTransport(DispatchingService service, FakeTraffic traffic)
    {
        var id = await service.RequestTransportAsync(Order(), None);
        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);
        traffic.SetAgv(S, AgvState.Idle, carrierRaised: false);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.ReportTransferCompletedAsync(id, None);
        traffic.SetAgv(S, AgvState.Idle, carrierRaised: true);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);
        return id;
    }

    /// <summary>搬送を最後まで進める。</summary>
    private static async Task CompleteTransport(DispatchingService service, FakeTraffic traffic, TransportId id)
    {
        traffic.SetAgv(D, AgvState.Moving);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, D), None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.ReportTransferCompletedAsync(id, None);
        traffic.SetAgv(D);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);
    }

    // ---- 搬送の流れ ----

    [Fact]
    public async Task 頼まれた搬送を待機中のAGVに割り当てて搬送元へ動かす()
    {
        var (service, traffic, _) = Setup(A);

        var id = await service.RequestTransportAsync(Order(), None);

        Assert.Equal([(Agv, S, $"transport:{id}")], traffic.Moves);
    }

    [Fact]
    public async Task 搬送元に着いたらキャリアを下げ下がったら搬送の許可を頼む()
    {
        var (service, traffic, events) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);

        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);
        Assert.Equal([Agv], traffic.Lowers);

        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        var permission = Assert.Single(events.Of<TransferPermissionRequested>());
        Assert.Equal((id, "station-1", TransferSide.Load), (permission.Transport, permission.Endpoint, permission.Side));
    }

    [Fact]
    public async Task 受け渡しが終わったらキャリアを上げ上がったら積み込んだことを知らせて搬送先へ動かす()
    {
        var (service, traffic, events) = Setup(A);

        var id = await LoadedTransport(service, traffic);

        Assert.Equal([Agv], traffic.Raises);
        Assert.Equal(id, Assert.Single(events.Of<CargoLoaded>()).Transport);
        Assert.Equal((Agv, D, $"transport:{id}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 搬送先で降ろしたら降ろしたことと完了を知らせ次の搬送を割り当てる()
    {
        var (service, traffic, events) = Setup(A);
        var first = await LoadedTransport(service, traffic);
        var second = await service.RequestTransportAsync(Order(20), None); // AGV は使用中なので待つ

        await CompleteTransport(service, traffic, first);

        Assert.Equal(first, Assert.Single(events.Of<CargoUnloaded>()).Transport);
        Assert.Equal(first, Assert.Single(events.Of<TransportCompleted>()).Transport);
        Assert.Equal((Agv, S, $"transport:{second}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 割り当てられるAGVがいなければ待ちAGVが空いたら割り当てる()
    {
        var (service, traffic, _) = Setup(A);
        traffic.SetAgv(A, AgvState.EmergencyStopped);
        await service.RequestTransportAsync(Order(), None);
        Assert.Empty(traffic.Moves);

        traffic.SetAgv(A);
        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);

        Assert.Single(traffic.Moves);
    }

    [Fact]
    public async Task 位置リセットが完了して待機中になったら割り当てる()
    {
        var (service, traffic, _) = Setup(A);
        traffic.SetAgv(A, AgvState.ResettingPosition);
        await service.RequestTransportAsync(Order(), None);

        traffic.SetAgv(A);
        await service.OnAgvPositionResetCompletedAsync(new AgvPositionResetCompleted(Meta(), Agv), None);

        Assert.Single(traffic.Moves);
    }

    [Fact]
    public async Task 割り当て前の搬送は取りやめられる()
    {
        var (service, traffic, _) = Setup(A);
        traffic.SetAgv(A, AgvState.EmergencyStopped);
        var id = await service.RequestTransportAsync(Order(), None);

        await service.CancelTransportAsync(id, None);
        traffic.SetAgv(A);
        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);

        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task 知らない搬送の受け渡しの完了は受け付けない()
    {
        var (service, _, _) = Setup(A);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReportTransferCompletedAsync(new TransportId(99), None));
    }

    // ---- 一時停止・中断・警告 ----

    [Fact]
    public async Task 一時停止中は次のコマンドを出さず再開したら出す()
    {
        var (service, traffic, _) = Setup(A);
        await service.RequestTransportAsync(Order(), None);
        await service.OnAgvStoppedAsync(new AgvStopped(Meta(), Agv), None);

        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);
        Assert.Empty(traffic.Lowers);

        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);
        Assert.Equal([Agv], traffic.Lowers);
    }

    [Fact]
    public async Task 異常になったら搬送を中断して知らせる()
    {
        var (service, _, events) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);

        await service.OnAgvFaultedAsync(new AgvFaulted(Meta(), Agv), None);

        Assert.Equal(id, Assert.Single(events.Of<TransportInterrupted>()).Transport);
    }

    [Fact]
    public async Task 経路が見つからなければ搬送を中断して知らせる()
    {
        var (service, traffic, events) = Setup(A);
        traffic.NextMoveResult = MoveResult.NoRoute;

        var id = await service.RequestTransportAsync(Order(), None);

        Assert.Equal(id, Assert.Single(events.Of<TransportInterrupted>()).Transport);
    }

    [Fact]
    public async Task 予約が取れずに待つときは警告する()
    {
        var (service, traffic, events) = Setup(A);
        traffic.NextMoveResult = MoveResult.Waiting;

        await service.RequestTransportAsync(Order(), None);

        var blocked = Assert.Single(events.Of<MoveBlocked>());
        Assert.Equal((Agv, S), (blocked.Agv, blocked.Destination));
    }

    // ---- 充電 ----

    [Fact]
    public async Task 待機中に充電残量が下回ったら一番近い空いている充電ポートへ向かわせる()
    {
        var (service, traffic, _) = Setup(A);

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);

        Assert.Equal([(Agv, CP, $"charge:{Agv}")], traffic.Moves);
    }

    [Fact]
    public async Task 行ける充電ポートがなければ知らせる()
    {
        var (service, traffic, events) = Setup(A);
        traffic.ReservedByOthers.Add(CP);

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);

        Assert.Empty(traffic.Moves);
        Assert.Equal(Agv, Assert.Single(events.Of<NoChargingPortAvailable>()).Agv);
    }

    [Fact]
    public async Task 充電中は割り当てず割り当てに戻す値まで回復したら割り当てる()
    {
        var (service, traffic, _) = Setup(A);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);
        traffic.SetAgv(CP);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, CP), None);

        await service.RequestTransportAsync(Order(), None);
        Assert.Single(traffic.Moves); // 充電ポートへの移動だけ

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 80), None);
        Assert.Equal(S, traffic.Moves[^1].To);
    }

    [Fact]
    public async Task 搬送の途中で下回ったら搬送を終えてから充電ポートへ向かわせる()
    {
        var (service, traffic, _) = Setup(A);
        var id = await LoadedTransport(service, traffic);

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);
        Assert.NotEqual(CP, traffic.Moves[^1].To);

        await CompleteTransport(service, traffic, id);
        Assert.Equal(CP, traffic.Moves[^1].To);
    }

    [Fact]
    public async Task 充電ポートへ向かう途中で異常になったら位置リセットのあとにもう一度探して向かわせる()
    {
        var (service, traffic, _) = Setup(A);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);
        await service.OnAgvFaultedAsync(new AgvFaulted(Meta(), Agv), None);

        traffic.SetAgv(A);
        await service.OnAgvPositionResetCompletedAsync(new AgvPositionResetCompleted(Meta(), Agv), None);

        Assert.Equal(2, traffic.Moves.Count(m => m.To == CP));
    }

    [Fact]
    public async Task 充電残量の通知がまだなくても割り当てる()
    {
        var (service, traffic, _) = Setup(A);

        await service.RequestTransportAsync(Order(), None);

        Assert.Single(traffic.Moves);
    }
}
