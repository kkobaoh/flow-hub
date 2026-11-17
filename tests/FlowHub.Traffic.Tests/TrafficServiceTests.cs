using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

public class TrafficServiceTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly PanelId A = new("A");
    private static readonly PanelId B = new("B");
    private static readonly PanelId T = new("T");
    private static readonly PanelId X = new("X");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// A →(0度) B →(0度) T（受け渡しパネル）。X は離れ島。
    /// </summary>
    private static InMemoryMapStore MapStore()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [
                new Panel(A, PanelType.Normal, false),
                new Panel(B, PanelType.Normal, false),
                new Panel(T, PanelType.Transfer, true),
                new Panel(X, PanelType.Normal, false),
            ],
            [
                new Connection(A, B, 800, new Heading(0), OneWay: false),
                new Connection(B, T, 800, new Heading(0), OneWay: false),
            ]).Snapshot!);
        return store;
    }

    private static async Task<(TrafficService Service, RecordingPublisher Events, InMemoryMapStore Map)> ReadyAt(Pose pose)
    {
        var map = MapStore();
        var events = new RecordingPublisher();
        var service = new TrafficService(map, events, new FixedTimeProvider());
        service.RegisterAgv(Agv, Motion);
        await service.ResetPositionAsync(Agv, pose, CancellationToken.None);
        await service.HandleCommandRequestAsync(Agv, -1, CancellationToken.None);
        await service.HandlePositionResetCompletedAsync(Agv, 1, CancellationToken.None);
        events.Published.Clear();
        return (service, events, map);
    }

    // ---- AGV を動かす ----

    [Fact]
    public async Task 頼まれたパネルへの経路を求めて動作コマンドを用意する()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));

        var result = await service.MoveAgvAsync(Agv, T, "transport-1", CancellationToken.None);

        Assert.Equal(MoveResult.Started, result);
        var command = Assert.IsType<MotionCommand>(Assert.Single(await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None)));
        Assert.Equal(2, command.Steps.Count);
    }

    [Fact]
    public async Task 着いたら着いたことを流れのIDを付けて知らせる()
    {
        var (service, events, _) = await ReadyAt(new Pose("A", 0));
        await service.MoveAgvAsync(Agv, T, "transport-1", CancellationToken.None);
        await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None);

        await service.HandleMoveCompletedAsync(Agv, 2, MoveDirection.Forward, B, CancellationToken.None);
        Assert.Empty(events.Published);
        await service.HandleMoveCompletedAsync(Agv, 2, MoveDirection.Forward, T, CancellationToken.None);

        var arrived = Assert.IsType<AgvArrived>(Assert.Single(events.Published));
        Assert.Equal(Agv, arrived.Agv);
        Assert.Equal(T, arrived.Panel);
        Assert.Equal("transport-1", arrived.Metadata.CorrelationId);
    }

    [Fact]
    public async Task すでにそのパネルにいれば動かさずにすぐ着いたと知らせる()
    {
        var (service, events, _) = await ReadyAt(new Pose("T", 0));

        var result = await service.MoveAgvAsync(Agv, T, "transport-1", CancellationToken.None);

        Assert.Equal(MoveResult.AlreadyThere, result);
        Assert.IsType<AgvArrived>(Assert.Single(events.Published));
        Assert.Empty(await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None));
    }

    [Fact]
    public async Task 経路が見つからなければ受け付けない()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));

        var result = await service.MoveAgvAsync(Agv, X, "transport-1", CancellationToken.None);

        Assert.Equal(MoveResult.NoRoute, result);
        Assert.Empty(await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None));
    }

    [Fact]
    public async Task 移動コストを求める()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));

        Assert.Equal(1600.0 / 500, service.EstimateMoveCost(Agv, T)!.Value.TotalSeconds, precision: 6);
        Assert.Null(service.EstimateMoveCost(Agv, X));
    }

    // ---- キャリア ----

    [Fact]
    public async Task キャリアを下げるときはマップの設定の高さを入れる()
    {
        var (service, _, map) = await ReadyAt(new Pose("T", 0));
        map.SetCarrierHeight(T, 120, Now);

        await service.LowerCarrierAsync(Agv, "transport-1", CancellationToken.None);

        Assert.Equal([new LowerCarrierCommand(2, 120)], await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None));
    }

    [Fact]
    public async Task 高さが設定されていなければ測定させて測定した高さをマップに保存する()
    {
        var (service, _, map) = await ReadyAt(new Pose("T", 0));

        await service.LowerCarrierAsync(Agv, "transport-1", CancellationToken.None);
        Assert.Equal([new LowerCarrierCommand(2, null)], await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None));

        await service.HandleCarrierHeightMeasuredAsync(Agv, 2, 118, CancellationToken.None);
        Assert.Equal(118, map.CarrierHeightOf(T));
    }

    [Fact]
    public async Task キャリアが下がったと上がったを知らせる()
    {
        var (service, events, _) = await ReadyAt(new Pose("T", 0));
        await service.LowerCarrierAsync(Agv, "transport-1", CancellationToken.None);
        await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None);

        await service.HandleCarrierLoweredAsync(Agv, 2, CancellationToken.None);
        await service.RaiseCarrierAsync(Agv, "transport-1", CancellationToken.None);
        await service.HandleCommandRequestAsync(Agv, 2, CancellationToken.None);
        await service.HandleCarrierRaisedAsync(Agv, 3, CancellationToken.None);

        Assert.Collection(
            events.Published,
            e => Assert.Equal("transport-1", Assert.IsType<CarrierLowered>(e).Metadata.CorrelationId),
            e => Assert.IsType<CarrierRaised>(e));
    }

    [Fact]
    public async Task 予期しないキャリアの通知では知らせない()
    {
        var (service, events, _) = await ReadyAt(new Pose("T", 0));

        await service.HandleCarrierLoweredAsync(Agv, 2, CancellationToken.None);

        Assert.Empty(events.Published);
    }

    // ---- 止まった・再開した・異常 ----

    [Fact]
    public async Task 緊急停止したら止まったことをアラームクリアが完了したら再開したことを知らせる()
    {
        var (service, events, _) = await ReadyAt(new Pose("A", 0));

        await service.EmergencyStopAsync(Agv, CancellationToken.None);
        await service.ClearAlarmAsync(Agv, CancellationToken.None);
        await service.HandleAlarmClearedAsync(Agv, 3, CancellationToken.None);

        Assert.Collection(
            events.Published,
            e => Assert.Equal(Agv, Assert.IsType<AgvStopped>(e).Agv),
            e => Assert.Equal(Agv, Assert.IsType<AgvResumed>(e).Agv));
    }

    [Fact]
    public async Task 位置がずれた可能性のあるエラーでは異常になったことを知らせる()
    {
        var (service, events, _) = await ReadyAt(new Pose("A", 0));

        await service.HandleAgvErrorAsync(Agv, AgvErrorKind.CrossMissed, CancellationToken.None);

        Assert.IsType<AgvFaulted>(Assert.Single(events.Published));
    }

    [Fact]
    public async Task 充電残量の通知をそのまま知らせる()
    {
        var (service, events, _) = await ReadyAt(new Pose("A", 0));

        await service.HandleBatteryChangedAsync(Agv, 42, CancellationToken.None);

        Assert.Equal(42, Assert.IsType<AgvBatteryChanged>(Assert.Single(events.Published)).Percent);
    }

    // ---- 状態の参照 ----

    [Fact]
    public async Task AGVの状態を参照できる()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));

        var view = service.GetAgv(Agv);

        Assert.Equal(new AgvView(Agv, new Pose("A", 0), AgvState.Idle, CarrierRaised: true, CumulativeDistanceMm: 0), view);
    }

    [Fact]
    public async Task 経路の予約が取れなければ待つ()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));
        var other = new AgvId("AGV-2");
        service.RegisterAgv(other, Motion);
        await service.ResetPositionAsync(other, new Pose("B", 0), CancellationToken.None); // B を予約している

        var result = await service.MoveAgvAsync(Agv, T, "transport-1", CancellationToken.None);

        Assert.Equal(MoveResult.Waiting, result);
    }

    [Fact]
    public async Task ほかのAGVが予約しているパネルには位置リセットできない()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));
        var other = new AgvId("AGV-2");
        service.RegisterAgv(other, Motion);

        Assert.False(await service.ResetPositionAsync(other, new Pose("A", 0), CancellationToken.None));
    }

    [Fact]
    public async Task 回転から始まる経路では回転の完了を受けて次の区間を渡し着いたら知らせる()
    {
        // A で 90度を向いている。0度の方向の B へは、まず 0度へ回転してから前進する
        var (service, events, _) = await ReadyAt(new Pose("A", 90));
        await service.MoveAgvAsync(Agv, B, "transport-1", CancellationToken.None);
        Assert.Equal(
            [new MotionCommand(2, [new RotateCommand(0)])],
            await service.HandleCommandRequestAsync(Agv, 1, CancellationToken.None));

        await service.HandleRotationCompletedAsync(Agv, 2, 0, CancellationToken.None);
        Assert.Empty(events.Published);
        Assert.Equal(
            [new MotionCommand(3, [new ForwardCommand(800, B)])],
            await service.HandleCommandRequestAsync(Agv, 2, CancellationToken.None));

        await service.HandleMoveCompletedAsync(Agv, 3, MoveDirection.Forward, B, CancellationToken.None);
        Assert.IsType<AgvArrived>(Assert.Single(events.Published));
    }

    [Fact]
    public async Task 再起動したAGVからの位置の取得には最後に把握していた位置を返す()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));

        Assert.Equal(new Pose("A", 0), await service.HandlePositionRequestAsync(Agv, CancellationToken.None));
    }

    [Fact]
    public async Task 受信の完了の通知はログに残すだけ()
    {
        var (service, events, _) = await ReadyAt(new Pose("A", 0));

        var outcome = await service.HandleMotionReceivedAsync(Agv, 1, CancellationToken.None);

        Assert.Equal(NotificationOutcome.Logged, outcome);
        Assert.Empty(events.Published);
    }

    [Fact]
    public void 位置がわからないAGVの移動コストは求められない()
    {
        using var service = new TrafficService(MapStore(), new RecordingPublisher(), new FixedTimeProvider());
        service.RegisterAgv(Agv, Motion);

        Assert.Null(service.EstimateMoveCost(Agv, T));
    }

    [Fact]
    public async Task ほかのAGVがそのパネルを予約しているかを問い合わせられる()
    {
        var (service, _, _) = await ReadyAt(new Pose("A", 0));
        var other = new AgvId("AGV-2");
        service.RegisterAgv(other, Motion);
        await service.ResetPositionAsync(other, new Pose("T", 0), CancellationToken.None);

        Assert.True(service.IsReservedByOther(T, Agv));
        Assert.False(service.IsReservedByOther(T, other)); // 自分の予約
        Assert.False(service.IsReservedByOther(B, Agv)); // 誰も予約していない
    }

    [Fact]
    public async Task 位置リセットが完了したら知らせる()
    {
        var map = MapStore();
        var events = new RecordingPublisher();
        using var service = new TrafficService(map, events, new FixedTimeProvider());
        service.RegisterAgv(Agv, Motion);
        await service.ResetPositionAsync(Agv, new Pose("A", 0), CancellationToken.None);
        await service.HandleCommandRequestAsync(Agv, -1, CancellationToken.None);

        await service.HandlePositionResetCompletedAsync(Agv, 1, CancellationToken.None);

        Assert.Equal(Agv, Assert.IsType<AgvPositionResetCompleted>(Assert.Single(events.Published)).Agv);
    }

    [Fact]
    public void 配車からはインターフェースを通して使える()
    {
        using var service = new TrafficService(MapStore(), new RecordingPublisher(), new FixedTimeProvider());

        Assert.IsAssignableFrom<ITrafficService>(service);
    }

    [Fact]
    public void 同じAGVは二重に登録できない()
    {
        using var service = new TrafficService(MapStore(), new RecordingPublisher(), new FixedTimeProvider());
        service.RegisterAgv(Agv, Motion);

        Assert.Throws<InvalidOperationException>(() => service.RegisterAgv(Agv, Motion));
    }

    [Fact]
    public void 登録していないAGVは扱えない()
    {
        var service = new TrafficService(MapStore(), new RecordingPublisher(), new FixedTimeProvider());

        Assert.Throws<InvalidOperationException>(() => service.GetAgv(Agv));
    }
}
