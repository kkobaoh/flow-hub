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

        /// <summary>動かすときに渡された、搬送の必要時刻（Moves と同じ順）。</summary>
        public List<DateTimeOffset?> NeededBys { get; } = [];

        public List<AgvId> Lowers { get; } = [];

        public List<AgvId> Raises { get; } = [];

        public AgvView GetAgv(AgvId id) => Agvs[id];

        public TimeSpan? EstimateMoveCost(AgvId id, PanelId destination) => TimeSpan.FromSeconds(10);

        public bool IsReservedByOther(PanelId panel, AgvId self) => ReservedByOthers.Contains(panel);

        public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, DateTimeOffset? neededBy, string correlationId, CancellationToken cancellationToken)
        {
            Moves.Add((id, destination, correlationId));
            NeededBys.Add(neededBy);
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

        public List<(AgvId Agv, ControlStatus Target)> ControlChanges { get; } = [];

        /// <summary>通り道を塞いでいる AGV をどかしてよいかの切り替え（切り替えた順）。</summary>
        public List<bool> EvacuationChanges { get; } = [];

        public void SetEvacuationEnabled(bool enabled) => EvacuationChanges.Add(enabled);

        /// <summary>止めた AGV（止めた順）。</summary>
        public List<AgvId> Stops { get; } = [];

        public Task StopAgvAsync(AgvId id, string correlationId, CancellationToken cancellationToken)
        {
            Stops.Add(id);
            return Task.CompletedTask;
        }

        public Task<bool> ChangeControlStatusAsync(AgvId id, ControlStatus target, Pose? position, CancellationToken cancellationToken)
        {
            ControlChanges.Add((id, target));
            Agvs[id] = Agvs[id] with { Control = target };
            return Task.FromResult(true);
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
        await AdvanceToLoaded(service, traffic, id);
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

    // ---- AGV の登録を変える・消す（AGV と AGV の種類の登録の画面） ----

    [Fact]
    public async Task 積んだまま止まっているAGVと搬送を割り当てたAGVは外さずほかは外せる()
    {
        var (service, traffic, _) = Setup(A);
        var id = await LoadedTransport(service, traffic);
        Assert.Equal(AgvRemovalResult.HasTransport, service.UnregisterAgv(Agv)); // 搬送を割り当てている

        await service.CancelAsync(id, None); // 積んだまま止まる
        Assert.Equal(AgvRemovalResult.Loaded, service.UnregisterAgv(Agv));
        Assert.True(service.IsLoaded(Agv));

        service.RegisterAgv(new AgvId("AGV-2"), CarrierX, 20, 80);
        Assert.Equal(AgvRemovalResult.Unregistered, service.UnregisterAgv(new AgvId("AGV-2")));
        Assert.Equal(AgvRemovalResult.NotFound, service.UnregisterAgv(new AgvId("AGV-2")));
    }

    [Fact]
    public async Task キャリアの種類を変えたら次の割り当てから新しいキャリアの種類で選ぶ()
    {
        var (service, _, _) = Setup(A);
        service.UpdateCarrier(Agv, new CarrierTypeId("Y"));

        await service.RequestTransportAsync(Order(), None); // キャリアの種類 X の搬送

        Assert.Single(service.ListTransports(), t => t.Progress is WaitingForAgv); // Y の AGV には割り当てない
    }

    // ---- 搬送の一覧（画面に出す） ----

    [Fact]
    public async Task 搬送の一覧は待ち行列と割り当て済みの搬送を番号の順に出し終わったものは出さない()
    {
        var (service, traffic, _) = Setup(A);
        var first = await service.RequestTransportAsync(Order(20), None); // AGV を割り当てる
        var second = await service.RequestTransportAsync(Order(10) with { CargoRefs = ["C-2", "C-3"] }, None); // AGV がいないので待つ

        Assert.Equal(
            [
                new TransportListing(first, "ToolPlanning", ["C-1"], new SourceTransferPoint(S, "station-1"), D, "device-1", Now.AddMinutes(20), new AssignedTo(Agv, AssignedTransportStage.MovingToSource, Paused: false)),
                new TransportListing(second, "ToolPlanning", ["C-2", "C-3"], new SourceTransferPoint(S, "station-1"), D, "device-1", Now.AddMinutes(10), new WaitingForAgv()),
            ],
            service.ListTransports());

        await service.CancelAsync(second, None);
        await AdvanceToLoaded(service, traffic, first);
        await CompleteTransport(service, traffic, first);
        Assert.Empty(service.ListTransports());
    }

    // ---- 割り当て済みの搬送の様子（画面に出す） ----

    [Fact]
    public async Task 割り当て済みの搬送の番号と搬送元と搬送先と段階がわかる()
    {
        var (service, traffic, _) = Setup(A);
        Assert.Empty(service.ListAssignments());

        var id = await service.RequestTransportAsync(Order(), None);
        var moving = Assert.Single(service.ListAssignments());
        await AdvanceToLoaded(service, traffic, id);
        var carrying = Assert.Single(service.ListAssignments());

        Assert.Equal(new AgvAssignment(Agv, id, new SourceTransferPoint(S, "station-1"), D, "device-1", AssignedTransportStage.MovingToSource, Paused: false), moving);
        Assert.Equal(AssignedTransportStage.MovingToDestination, carrying.Stage);
    }

    [Fact]
    public async Task 運転を停止して一時停止した搬送は一時停止中とわかり終わった搬送は出さない()
    {
        var (service, traffic, _) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);

        await service.StopAsync(None);
        Assert.True(Assert.Single(service.ListAssignments()).Paused);

        await service.StartAsync(None);
        await AdvanceToLoaded(service, traffic, id);
        await CompleteTransport(service, traffic, id);
        Assert.Empty(service.ListAssignments());
    }

    /// <summary>頼んだ搬送を、搬送元の受け渡しを終えてキャリアを上げるところまで進める（頼むところは済んでいる）。</summary>
    private static async Task AdvanceToLoaded(DispatchingService service, FakeTraffic traffic, TransportId id)
    {
        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);
        traffic.SetAgv(S, AgvState.Idle, carrierRaised: false);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.ReportTransferCompletedAsync(id, None);
        traffic.SetAgv(S, AgvState.Idle, carrierRaised: true);
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
    public async Task 搬送でAGVを動かすときは搬送の必要時刻を交通整理に渡す()
    {
        var (service, traffic, _) = Setup(A);

        await service.RequestTransportAsync(Order(neededInMinutes: 15), None);

        Assert.Equal([Now.AddMinutes(15)], traffic.NeededBys);
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
        Assert.Equal("ToolPlanning", permission.Requester); // 要求元を入れる
        Assert.Equal(["C-1"], permission.CargoRefs);
    }

    [Fact]
    public async Task 受け渡しが終わったらキャリアを上げ上がったら搬送先へ動かす()
    {
        var (service, traffic, _) = Setup(A);

        var id = await LoadedTransport(service, traffic);

        Assert.Equal([Agv], traffic.Raises);
        Assert.Equal((Agv, D, $"transport:{id}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 搬送先で降ろしたら完了を知らせ次の搬送を割り当てる()
    {
        var (service, traffic, events) = Setup(A);
        var first = await LoadedTransport(service, traffic);
        var second = await service.RequestTransportAsync(Order(20), None); // AGV は使用中なので待つ

        await CompleteTransport(service, traffic, first);

        Assert.Equal("ToolPlanning", Assert.Single(events.Of<TransportCompleted>()).Requester);
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

        var cancelled = await service.CancelTransportAsync(id, None);
        traffic.SetAgv(A);
        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);

        Assert.True(cancelled);
        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task 割り当てたあとの搬送は取りやめずにそのまま運ぶ()
    {
        var (service, traffic, _) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);

        var cancelled = await service.CancelTransportAsync(id, None);

        Assert.False(cancelled);
        Assert.Single(traffic.Moves);
    }

    [Fact]
    public async Task 知らない搬送は取りやめられない()
    {
        var (service, _, _) = Setup(A);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelTransportAsync(new TransportId(99), None));
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
        Assert.Equal([null], traffic.NeededBys); // 搬送のない移動なので、必要時刻はない
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
    public async Task 中断の知らせにも要求元を入れる()
    {
        var (service, _, events) = Setup(A);
        await service.RequestTransportAsync(Order(), None);

        await service.OnAgvFaultedAsync(new AgvFaulted(Meta(), Agv), None);

        Assert.Equal("ToolPlanning", Assert.Single(events.Of<TransportInterrupted>()).Requester);
    }

    [Fact]
    public void ほかのコンテキストからはインターフェースを通して使える()
    {
        var (service, _, _) = Setup(A);

        Assert.IsAssignableFrom<IDispatchingService>(service);
    }

    [Fact]
    public async Task 充電残量の通知がまだなくても割り当てる()
    {
        var (service, traffic, _) = Setup(A);

        await service.RequestTransportAsync(Order(), None);

        Assert.Single(traffic.Moves);
    }

    // ---- コントロールステータス ----

    [Theory]
    [InlineData(ControlStatus.AssignmentPaused)]
    [InlineData(ControlStatus.Manual)]
    [InlineData(ControlStatus.Locked)]
    public async Task オートでないAGVには搬送を割り当てない(ControlStatus status)
    {
        var (service, traffic, _) = Setup(A);
        await service.ChangeControlStatusAsync(Agv, status, position: null, None);

        await service.RequestTransportAsync(Order(), None);

        Assert.Empty(traffic.Moves);
    }

    [Fact]
    public async Task オートに戻したら待っている搬送を割り当てる()
    {
        var (service, traffic, _) = Setup(A);
        await service.ChangeControlStatusAsync(Agv, ControlStatus.AssignmentPaused, position: null, None);
        await service.RequestTransportAsync(Order(), None);

        await service.ChangeControlStatusAsync(Agv, ControlStatus.Auto, position: null, None);

        Assert.Equal(S, Assert.Single(traffic.Moves).To);
    }

    [Theory]
    [InlineData(ControlStatus.Manual)]
    [InlineData(ControlStatus.Locked)]
    [InlineData(ControlStatus.Detached)]
    public async Task 搬送を割り当てているAGVはマニュアルとロックと切り離しに切り替えない(ControlStatus status)
    {
        var (service, traffic, _) = Setup(A);
        await service.RequestTransportAsync(Order(), None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeControlStatusAsync(Agv, status, position: null, None));
        Assert.Empty(traffic.ControlChanges);
    }

    [Fact]
    public async Task 搬送を割り当てているAGVでも割り当て停止には切り替えられる()
    {
        var (service, traffic, _) = Setup(A);
        await service.RequestTransportAsync(Order(), None);

        await service.ChangeControlStatusAsync(Agv, ControlStatus.AssignmentPaused, position: null, None);

        Assert.Equal([(Agv, ControlStatus.AssignmentPaused)], traffic.ControlChanges);
    }

    [Fact]
    public async Task 割り当て停止のAGVも充電ポートへ向かわせる()
    {
        var (service, traffic, _) = Setup(A);
        await service.ChangeControlStatusAsync(Agv, ControlStatus.AssignmentPaused, position: null, None);

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);

        Assert.Equal(CP, Assert.Single(traffic.Moves).To);
    }

    [Fact]
    public async Task 充電ポートへ向かう途中でマニュアルにしたらオートに戻したときにもう一度探して向かわせる()
    {
        var (service, traffic, _) = Setup(A);
        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);

        await service.ChangeControlStatusAsync(Agv, ControlStatus.Manual, position: null, None); // 交通整理が行き先を捨てる
        await service.ChangeControlStatusAsync(Agv, ControlStatus.Auto, position: null, None);

        Assert.Equal(2, traffic.Moves.Count(m => m.To == CP));
    }

    [Fact]
    public async Task マニュアルの間は充電ポートへ向かわせずオートに戻したら向かわせる()
    {
        var (service, traffic, _) = Setup(A);
        await service.ChangeControlStatusAsync(Agv, ControlStatus.Manual, position: null, None);

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);
        Assert.Empty(traffic.Moves);

        await service.ChangeControlStatusAsync(Agv, ControlStatus.Auto, position: null, None);
        Assert.Equal(CP, Assert.Single(traffic.Moves).To);
    }

    // ---- 運転状態 ----

    /// <summary>停止にしたときに待機パネルへ向かわせた移動を除いた移動。</summary>
    private static (AgvId Agv, PanelId To, string CorrelationId)[] NonParkingMoves(FakeTraffic traffic) =>
        traffic.Moves.Where(m => !m.CorrelationId.StartsWith("park:", StringComparison.Ordinal)).ToArray();

    [Fact]
    public async Task 停止中は搬送を割り当てずスタートしたら割り当てる()
    {
        var (service, traffic, _) = Setup(A);
        await service.StopAsync(None);

        var id = await service.RequestTransportAsync(Order(), None);
        var whileStopped = NonParkingMoves(traffic).Length;
        await service.StartAsync(None);

        Assert.Equal(0, whileStopped); // 頼みは受け付けて、待ち行列に入れておく
        Assert.Equal([(Agv, S, $"transport:{id}")], NonParkingMoves(traffic));
    }

    [Fact]
    public async Task 停止中は充電ポートへ向かわせずスタートしたら向かわせる()
    {
        var (service, traffic, _) = Setup(A);
        await service.StopAsync(None);

        await service.OnAgvBatteryChangedAsync(new AgvBatteryChanged(Meta(), Agv, 19), None);
        var whileStopped = NonParkingMoves(traffic).Length;
        await service.StartAsync(None);

        Assert.Equal(0, whileStopped);
        Assert.Equal(CP, Assert.Single(NonParkingMoves(traffic)).To);
    }

    [Fact]
    public async Task 停止したら搬送の次のコマンドを出さずスタートしたら続きから出す()
    {
        var (service, traffic, _) = Setup(A);
        await service.RequestTransportAsync(Order(), None);
        await service.StopAsync(None);

        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);
        var whileStopped = traffic.Lowers.Count;
        await service.StartAsync(None);

        Assert.Equal(0, whileStopped); // 着いたことは受け付けるが、キャリアは下げない
        Assert.Equal([Agv], traffic.Lowers);
    }

    [Fact]
    public async Task 停止中は緊急停止から再開しても次のコマンドを出さない()
    {
        var (service, traffic, _) = Setup(A);
        await service.RequestTransportAsync(Order(), None);
        await service.StopAsync(None);
        await service.OnAgvStoppedAsync(new AgvStopped(Meta(), Agv), None);
        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);

        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);
        var afterResumed = traffic.Lowers.Count;
        await service.StartAsync(None);

        Assert.Equal(0, afterResumed);
        Assert.Equal([Agv], traffic.Lowers);
    }

    [Fact]
    public async Task スタートしても緊急停止中なら次のコマンドを出さず再開したら出す()
    {
        var (service, traffic, _) = Setup(A);
        await service.RequestTransportAsync(Order(), None);
        await service.StopAsync(None);
        await service.OnAgvStoppedAsync(new AgvStopped(Meta(), Agv), None);
        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);

        await service.StartAsync(None);
        var afterStarted = traffic.Lowers.Count;
        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);

        Assert.Equal(0, afterStarted);
        Assert.Equal([Agv], traffic.Lowers);
    }

    [Fact]
    public async Task 停止とスタートで交通整理がどかす動きを止めて戻す()
    {
        var (service, traffic, _) = Setup(A);

        await service.StopAsync(None);
        await service.StartAsync(None);

        Assert.Equal([false, true], traffic.EvacuationChanges);
    }

    [Fact]
    public async Task スタートしたら運転を始めたことを知らせ同じ状態への切り替えは何もしない()
    {
        var (service, traffic, events) = Setup(A);
        var initiallyRunning = service.IsRunning;

        await service.StartAsync(None); // すでにスタート
        await service.StopAsync(None);
        var stopped = service.IsRunning;
        await service.StopAsync(None); // すでに停止
        await service.StartAsync(None);

        Assert.True(initiallyRunning);
        Assert.False(stopped);
        Assert.True(service.IsRunning);
        Assert.Single(events.Of<OperationStarted>());
        Assert.Equal([false, true], traffic.EvacuationChanges);
    }

    // ---- 割り当てたあとの取りやめ ----

    /// <summary>搬送元に着いてキャリアを下げ始めたところまで進める（受け渡しの許可はまだ）。</summary>
    private static async Task<TransportId> LoweringAtSource(DispatchingService service, FakeTraffic traffic)
    {
        var id = await service.RequestTransportAsync(Order(), None);
        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None);
        return id;
    }

    private static TransportCanceled CanceledEvent(RecordingPublisher events) => Assert.Single(events.Of<TransportCanceled>());

    [Fact]
    public async Task 割り当て前の搬送も取りやめられる()
    {
        var (service, traffic, events) = Setup(A);
        traffic.SetAgv(A, AgvState.EmergencyStopped); // 割り当てられる AGV がいない
        var id = await service.RequestTransportAsync(Order(), None);

        var result = await service.CancelAsync(id, None);

        Assert.Equal(TransportCancelResult.Canceled, result);
        Assert.Empty(events.Of<TransportCanceled>()); // 割り当て前は、頼んだ側が自分で取りやめたので知らせない
    }

    [Fact]
    public async Task 搬送元へ移動中に取りやめるとAGVを止めて何も積まずに取りやめたことを知らせる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);

        var result = await service.CancelAsync(id, None);

        Assert.Equal(TransportCancelResult.Canceled, result);
        Assert.Equal([Agv], traffic.Stops);
        var canceled = CanceledEvent(events);
        Assert.Equal((id, "ToolPlanning", Agv, false), (canceled.Transport, canceled.Requester, canceled.Agv, canceled.Loaded));
    }

    [Fact]
    public async Task 何も積まずに取りやめたAGVにはほかの搬送を割り当てる()
    {
        var (service, traffic, _) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);
        await service.CancelAsync(id, None);

        var next = await service.RequestTransportAsync(Order(), None);

        Assert.Equal((Agv, S, $"transport:{next}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 搬送元で許可の前に取りやめるとキャリアが下がったら上げて止まり何も積まずに取りやめる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoweringAtSource(service, traffic);

        var result = await service.CancelAsync(id, None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        var beforeRaised = events.Of<TransportCanceled>().Count();
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(TransportCancelResult.Canceling, result);
        Assert.Empty(events.Of<TransferPermissionRequested>()); // 許可は出さない
        Assert.Equal([Agv], traffic.Raises);
        Assert.Equal(0, beforeRaised);
        Assert.False(CanceledEvent(events).Loaded);
    }

    [Fact]
    public async Task 搬送元で許可のあとに取りやめると受け渡しを終えてキャリアを上げたところで積んだまま取りやめる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoweringAtSource(service, traffic);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None); // 許可を頼んだ

        var result = await service.CancelAsync(id, None);
        await service.ReportTransferCompletedAsync(id, None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(TransportCancelResult.Canceling, result);
        Assert.Equal([Agv], traffic.Raises);
        Assert.True(CanceledEvent(events).Loaded);
        Assert.DoesNotContain(traffic.Moves, m => m.To == D); // 搬送先へは向かわない
    }

    [Fact]
    public async Task 搬送先へ移動中に取りやめるとAGVを止めて積んだまま取りやめる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoadedTransport(service, traffic);

        var result = await service.CancelAsync(id, None);

        Assert.Equal(TransportCancelResult.Canceled, result);
        Assert.Equal([Agv], traffic.Stops);
        Assert.True(CanceledEvent(events).Loaded);
    }

    [Fact]
    public async Task 積んだまま取りやめたAGVにはほかの搬送を割り当てない()
    {
        var (service, traffic, _) = Setup(A);
        var id = await LoadedTransport(service, traffic);
        await service.CancelAsync(id, None);
        var moves = traffic.Moves.Count;

        await service.RequestTransportAsync(Order(), None);

        Assert.Equal(moves, traffic.Moves.Count);
    }

    [Fact]
    public async Task 搬送先で許可の前に取りやめるとキャリアが下がったら上げて止まり積んだまま取りやめる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoadedTransport(service, traffic);
        traffic.SetAgv(D);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, D), None);

        var result = await service.CancelAsync(id, None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(TransportCancelResult.Canceling, result);
        Assert.Single(events.Of<TransferPermissionRequested>()); // 搬送元の許可だけ
        Assert.True(CanceledEvent(events).Loaded);
        Assert.Empty(events.Of<TransportCompleted>());
    }

    [Fact]
    public async Task 搬送先で許可のあとに取りやめると受け渡しを終えて完了として扱う()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoadedTransport(service, traffic);
        traffic.SetAgv(D);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, D), None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None); // 搬送先の許可を頼んだ

        var result = await service.CancelAsync(id, None);
        await service.ReportTransferCompletedAsync(id, None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(TransportCancelResult.Completing, result);
        Assert.Single(events.Of<TransportCompleted>());
        Assert.Empty(events.Of<TransportCanceled>());
    }

    [Fact]
    public async Task 中断した搬送と終えた搬送は取りやめられない()
    {
        var (service, traffic, _) = Setup(A);
        var interrupted = await service.RequestTransportAsync(Order(), None);
        await service.OnAgvFaultedAsync(new AgvFaulted(Meta(), Agv), None);

        var result = await service.CancelAsync(interrupted, None);

        Assert.Equal(TransportCancelResult.NotCancelable, result);
        Assert.Empty(traffic.Stops);
    }

    [Fact]
    public async Task 割り当てたあとの取りやめでも知らない搬送は取りやめられない()
    {
        var (service, _, _) = Setup(A);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(new TransportId(99), None));
    }

    [Fact]
    public async Task 停止中でも取りやめを受け付けキャリアを上げる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoweringAtSource(service, traffic);
        await service.StopAsync(None);

        var result = await service.CancelAsync(id, None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(TransportCancelResult.Canceling, result);
        Assert.Equal([Agv], traffic.Raises);
        Assert.False(CanceledEvent(events).Loaded);
    }

    [Fact]
    public async Task 緊急停止中に取りやめたらアラームクリアのあとにキャリアを上げる()
    {
        var (service, traffic, _) = Setup(A);
        var id = await LoweringAtSource(service, traffic);
        await service.OnAgvStoppedAsync(new AgvStopped(Meta(), Agv), None);

        await service.CancelAsync(id, None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        var whileStopped = traffic.Raises.Count;
        await service.OnAgvResumedAsync(new AgvResumed(Meta(), Agv), None);

        Assert.Equal(0, whileStopped);
        Assert.Equal([Agv], traffic.Raises);
    }

    [Fact]
    public async Task 停止中に着いてキャリアを下げる前なら取りやめると下げずにすぐ取りやめる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await service.RequestTransportAsync(Order(), None);
        await service.StopAsync(None);
        traffic.SetAgv(S);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, S), None); // キャリアを下げるコマンドは出さずにおく

        var result = await service.CancelAsync(id, None);

        Assert.Equal(TransportCancelResult.Canceled, result);
        Assert.Empty(traffic.Lowers);
        Assert.Empty(traffic.Raises);
        Assert.False(CanceledEvent(events).Loaded);
    }

    [Fact]
    public async Task 停止中にキャリアが下がって許可をまだ頼んでいなければ許可を出さずにキャリアを上げる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoweringAtSource(service, traffic);
        await service.StopAsync(None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None); // 許可は頼まずにおく

        var result = await service.CancelAsync(id, None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(TransportCancelResult.Canceling, result);
        Assert.Empty(events.Of<TransferPermissionRequested>());
        Assert.Equal([Agv], traffic.Raises);
        Assert.False(CanceledEvent(events).Loaded);
    }

    [Fact]
    public async Task 停止中に受け渡しを終えてキャリアを上げる前なら取りやめるとすぐにキャリアを上げる()
    {
        var (service, traffic, events) = Setup(A);
        var id = await LoweringAtSource(service, traffic);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.StopAsync(None);
        await service.ReportTransferCompletedAsync(id, None); // キャリアを上げるコマンドは出さずにおく
        var beforeCancel = traffic.Raises.Count;

        var result = await service.CancelAsync(id, None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal(0, beforeCancel);
        Assert.Equal(TransportCancelResult.Canceling, result);
        Assert.Equal([Agv], traffic.Raises);
        Assert.True(CanceledEvent(events).Loaded);
    }

    // ---- AGV 自身が搬送元の搬送 ----

    /// <summary>積み込んだあとに取りやめて、ものを積んだまま止まっている AGV にする（取りやめた搬送の ID は 1）。</summary>
    private static async Task CanceledWhileLoaded(DispatchingService service, FakeTraffic traffic)
    {
        var id = await LoadedTransport(service, traffic);
        await service.CancelAsync(id, None);
    }

    private static readonly TransportId LoadedBy = new(1);

    private static AgvTransportOrder FromAgvOrder() => new("ToolPlanning", ["C-1"], LoadedBy, D, "device-1", Now.AddMinutes(10));

    [Fact]
    public async Task AGV自身が搬送元の搬送は積み込みを飛ばして搬送先へ向かい降ろしたら完了する()
    {
        var (service, traffic, events) = Setup(A);
        await CanceledWhileLoaded(service, traffic);
        var lowersBefore = traffic.Lowers.Count;

        var id = await service.RequestTransportFromAgvAsync(FromAgvOrder(), None);
        var moved = traffic.Moves[^1];
        traffic.SetAgv(D);
        await service.OnAgvArrivedAsync(new AgvArrived(Meta(), Agv, D), None);
        await service.OnCarrierLoweredAsync(new CarrierLowered(Meta(), Agv), None);
        await service.ReportTransferCompletedAsync(id, None);
        await service.OnCarrierRaisedAsync(new CarrierRaised(Meta(), Agv), None);

        Assert.Equal((Agv, D, $"transport:{id}"), moved);
        Assert.Equal(lowersBefore + 1, traffic.Lowers.Count); // 搬送先でだけ下げる
        var permission = events.Of<TransferPermissionRequested>().Last();
        Assert.Equal((id, "device-1", TransferSide.Unload), (permission.Transport, permission.Endpoint, permission.Side));
        Assert.Equal(id, events.Of<TransportCompleted>().Last().Transport);
    }

    [Fact]
    public async Task 届けたらほかの搬送を割り当てられるようになる()
    {
        var (service, traffic, _) = Setup(A);
        await CanceledWhileLoaded(service, traffic);
        var id = await service.RequestTransportFromAgvAsync(FromAgvOrder(), None);
        await CompleteTransport(service, traffic, id);

        var next = await service.RequestTransportAsync(Order(), None);

        Assert.Equal((Agv, S, $"transport:{next}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 割り当て停止のあいだは積んだまま待ちオートに戻したら向かう()
    {
        var (service, traffic, _) = Setup(A);
        await CanceledWhileLoaded(service, traffic);
        await service.ChangeControlStatusAsync(Agv, ControlStatus.AssignmentPaused, position: null, None);

        var id = await service.RequestTransportFromAgvAsync(FromAgvOrder(), None);
        var whilePaused = traffic.Moves.Count(m => m.CorrelationId == $"transport:{id}");
        await service.ChangeControlStatusAsync(Agv, ControlStatus.Auto, position: null, None);

        Assert.Equal(0, whilePaused);
        Assert.Equal((Agv, D, $"transport:{id}"), traffic.Moves[^1]);
    }

    [Fact]
    public async Task 積んだまま止まっていないAGVを搬送元にした頼みは受け付けない()
    {
        var (service, _, _) = Setup(A);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestTransportFromAgvAsync(FromAgvOrder(), None));
    }

    [Fact]
    public async Task 同じAGVを搬送元にした頼みは1つだけ受け付ける()
    {
        var (service, traffic, _) = Setup(A);
        await CanceledWhileLoaded(service, traffic);
        await service.ChangeControlStatusAsync(Agv, ControlStatus.AssignmentPaused, position: null, None);
        await service.RequestTransportFromAgvAsync(FromAgvOrder(), None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestTransportFromAgvAsync(FromAgvOrder(), None));
    }

    [Fact]
    public async Task AGV自身が搬送元の搬送をまた積んだまま取りやめたらほかの搬送は割り当てない()
    {
        var (service, traffic, events) = Setup(A);
        await CanceledWhileLoaded(service, traffic);
        var id = await service.RequestTransportFromAgvAsync(FromAgvOrder(), None);

        await service.CancelAsync(id, None);
        var moves = traffic.Moves.Count;
        await service.RequestTransportAsync(Order(), None);

        Assert.True(events.Of<TransportCanceled>().Last().Loaded);
        Assert.Equal(moves, traffic.Moves.Count);
    }

    [Fact]
    public async Task 降ろしたあとも積んだままと頼まれたら完了しても積んだままとして扱う()
    {
        var (service, traffic, _) = Setup(A);
        await CanceledWhileLoaded(service, traffic);
        var id = await service.RequestTransportFromAgvAsync(FromAgvOrder() with { CargoRemains = true }, None);
        await CompleteTransport(service, traffic, id);
        var moves = traffic.Moves.Count;

        await service.RequestTransportAsync(Order(), None); // ほかの搬送は割り当てない
        var assignedOther = traffic.Moves.Count > moves;
        var back = await service.RequestTransportFromAgvAsync(FromAgvOrder() with { DestinationPanel = S, DestinationEndpoint = "station-1" }, None);

        Assert.False(assignedOther);
        Assert.Equal((Agv, S, $"transport:{back}"), traffic.Moves[^1]); // 残りを返す
    }

    [Fact]
    public async Task 届け終えた搬送で積んだものを指した頼みは同じAGVがまた積んでも受け付けない()
    {
        var (service, traffic, _) = Setup(A);
        await CanceledWhileLoaded(service, traffic); // 搬送 1 で積んだ
        var delivered = await service.RequestTransportFromAgvAsync(FromAgvOrder(), None); // 搬送 2
        await CompleteTransport(service, traffic, delivered);
        traffic.SetAgv(A);
        var again = await LoadedTransport(service, traffic); // 搬送 3 で、また積んだ
        await service.CancelAsync(again, None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestTransportFromAgvAsync(FromAgvOrder(), None));
        Assert.NotEqual(default, await service.RequestTransportFromAgvAsync(FromAgvOrder() with { LoadedBy = again }, None));
    }
}
