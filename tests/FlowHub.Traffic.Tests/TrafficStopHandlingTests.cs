using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 止まった AGV を障害物にして計画を立て直すこと（異常はすぐ、緊急停止中は1分）と、邪魔な AGV をどかすことのテスト。
/// </summary>
public class TrafficStopHandlingTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] CrossroadsPanels = ["N", "M", "S", "E", "W"];

    /// <summary>時刻を手で進められ、タイマーも、進めた時刻に合わせて動く。</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        public DateTimeOffset Now { get; private set; } = Start;

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            Now += by;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue();
            }
        }

        private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
                return true;
            }

            public void FireIfDue()
            {
                if (_due is { } due && clock.Now >= due)
                {
                    _due = null;
                    callback(state);
                }
            }

            public void Dispose() => _due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    /// <summary>
    /// 十字路。N →(180度) M →(180度) S と、E →(270度) M →(270度) W が、M で交わる。
    /// </summary>
    private static InMemoryMapStore Crossroads()
    {
        var store = new InMemoryMapStore();
        static PanelId P(string id) => new(id);
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

    private static async Task ReadyAsync(TrafficService service, AgvId agv, Pose pose)
    {
        service.RegisterAgv(agv, Motion);
        await service.ResetPositionAsync(agv, pose, None);
        await service.HandleCommandRequestAsync(agv, -1, None);
        await service.HandlePositionResetCompletedAsync(agv, 1, None);
    }

    /// <summary>
    /// AGV-2（E）は緊急停止中で、先に W への頼みを受けている（必要時刻が早い）。AGV-1（N）は S へ向かうが、十字路は AGV-2 の番なので待っている。
    /// </summary>
    private static async Task<(TrafficService Service, ManualTimeProvider Clock)> Agv1WaitingBehindStoppedAgv2Async()
    {
        var clock = new ManualTimeProvider();
        var service = new TrafficService(Crossroads(), new IgnoringPublisher(), clock);
        await ReadyAsync(service, Agv1, new Pose("N", 180));
        await ReadyAsync(service, Agv2, new Pose("E", 270));
        await service.EmergencyStopAsync(Agv2, None); // 番号 2
        await service.MoveAgvAsync(Agv2, new PanelId("W"), Start, "transport-2", None);
        await service.MoveAgvAsync(Agv1, new PanelId("S"), Start.AddMinutes(10), "transport-1", None);
        return (service, clock);
    }

    private static async Task<MotionCommand?> MotionOf(TrafficService service, AgvId agv, long lastReceived = 1) =>
        (await service.HandleCommandRequestAsync(agv, lastReceived, None)).OfType<MotionCommand>().SingleOrDefault();

    [Fact]
    public async Task 異常になったAGVはすぐに障害物にして計画を立て直す()
    {
        var (service, _) = await Agv1WaitingBehindStoppedAgv2Async();
        var beforeFault = await MotionOf(service, Agv1);

        await service.HandleAgvErrorAsync(Agv2, AgvErrorKind.CrossMissed, None);

        Assert.Null(beforeFault);
        Assert.NotNull(await MotionOf(service, Agv1));
    }

    [Fact]
    public async Task 異常で障害物にしたAGVは行き先を捨て位置リセットのあとも動かない()
    {
        var (service, _) = await Agv1WaitingBehindStoppedAgv2Async();
        await service.HandleAgvErrorAsync(Agv2, AgvErrorKind.CrossMissed, None);

        await service.ResetPositionAsync(Agv2, new Pose("E", 270), None); // 番号 3
        await service.HandleCommandRequestAsync(Agv2, 2, None);
        await service.HandlePositionResetCompletedAsync(Agv2, 3, None);

        Assert.Null(await MotionOf(service, Agv2, lastReceived: 3));
    }

    [Fact]
    public async Task 緊急停止中のAGVは1分たつまで障害物にせずたったら障害物にして計画を立て直す()
    {
        var (service, clock) = await Agv1WaitingBehindStoppedAgv2Async();

        clock.Advance(TimeSpan.FromSeconds(59));
        var before = await MotionOf(service, Agv1);
        clock.Advance(TimeSpan.FromSeconds(1));
        var after = await MotionOf(service, Agv1);

        Assert.Null(before);
        Assert.NotNull(after);
    }

    [Fact]
    public async Task 緊急停止が長引いて計画を立て直したら新しいコマンドができたAGVのコマンドを待っている要求を起こす()
    {
        var (service, clock) = await Agv1WaitingBehindStoppedAgv2Async();
        await MotionOf(service, Agv1); // 渡すコマンドはまだない
        var changed = service.NextChangeAsync(Agv1);

        clock.Advance(TimeSpan.FromMinutes(1));

        await changed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(await MotionOf(service, Agv1));
    }

    [Fact]
    public async Task 緊急停止が長引いて計画を立て直したらロックを外してから組み立ての後始末を呼ぶ()
    {
        var (service, clock) = await Agv1WaitingBehindStoppedAgv2Async();
        var called = new TaskCompletionSource();
        service.AttachAfterBackgroundWork(async cancellationToken =>
        {
            try
            {
                // ロックを持ったまま呼ばれていたら、ここで取れずに時間切れになる
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await service.HandlePositionRequestAsync(Agv1, timeout.Token);
                called.SetResult();
            }
            catch (OperationCanceledException exception)
            {
                called.SetException(exception);
            }
        });

        clock.Advance(TimeSpan.FromMinutes(1));

        await called.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 緊急停止が長引く前に再開したら組み立ての後始末を呼ばない()
    {
        var (service, clock) = await Agv1WaitingBehindStoppedAgv2Async();
        var calls = 0;
        service.AttachAfterBackgroundWork(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        await service.ClearAlarmAsync(Agv2, None); // 番号 3
        await service.HandleCommandRequestAsync(Agv2, 2, None);
        await service.HandleAlarmClearedAsync(Agv2, 3, None);

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ほかのAGVの通知で新しいコマンドができたらそのAGVのコマンドを待っている要求を起こす()
    {
        var (service, clock) = await Agv1WaitingBehindStoppedAgv2Async();
        clock.Advance(TimeSpan.FromMinutes(1));
        await MotionOf(service, Agv1); // AGV-1 が先に十字路を通る（番号 2）
        await service.ClearAlarmAsync(Agv2, None); // 番号 3
        await service.HandleCommandRequestAsync(Agv2, 2, None);
        await service.HandleAlarmClearedAsync(Agv2, 3, None);
        Assert.Null(await MotionOf(service, Agv2, lastReceived: 3)); // AGV-1 が通り過ぎるのを待っている
        var changed = service.NextChangeAsync(Agv2);

        // AGV-1 の通知で十字路が空き、AGV-2 に区間を渡す
        await service.HandleMoveCompletedAsync(Agv1, 2, MoveDirection.Forward, new PanelId("M"), None);
        await service.HandleMoveCompletedAsync(Agv1, 2, MoveDirection.Forward, new PanelId("S"), None);

        await changed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(await MotionOf(service, Agv2, lastReceived: 3));
    }

    [Fact]
    public async Task 緊急停止中で障害物にしたAGVはアラームクリアのあとに行き先への計画を立て直す()
    {
        var (service, clock) = await Agv1WaitingBehindStoppedAgv2Async();
        clock.Advance(TimeSpan.FromMinutes(1));
        await MotionOf(service, Agv1); // AGV-1 が先に十字路を通る（番号 2）

        await service.ClearAlarmAsync(Agv2, None); // 番号 3
        await service.HandleCommandRequestAsync(Agv2, 2, None);
        await service.HandleAlarmClearedAsync(Agv2, 3, None);
        var whileAgv1Passing = await MotionOf(service, Agv2, lastReceived: 3);
        await service.HandleMoveCompletedAsync(Agv1, 2, MoveDirection.Forward, new PanelId("M"), None);
        await service.HandleMoveCompletedAsync(Agv1, 2, MoveDirection.Forward, new PanelId("S"), None);
        var afterAgv1Passed = await MotionOf(service, Agv2, lastReceived: 3);

        Assert.Null(whileAgv1Passing);
        Assert.NotNull(afterAgv1Passed);
        Assert.Equal(new PanelId("W"), ((ForwardCommand)afterAgv1Passed!.Steps[^1]).To);
    }

    [Fact]
    public async Task 通り道を塞いでいる行き先のない動かせるAGVは待機パネルへどかしてから通す()
    {
        // N →(180度) M →(180度) S の通路の M に、行き先のない AGV-2 がいる。M の東（90度）に待機パネル P
        var store = new InMemoryMapStore();
        static PanelId P(string id) => new(id);
        store.Publish(MapSnapshot.Create(
            [
                new Panel(P("N"), PanelType.Normal, false),
                new Panel(P("M"), PanelType.Normal, false),
                new Panel(P("S"), PanelType.Normal, false),
                new Panel(P("P"), PanelType.Standby, false),
            ],
            [
                new Connection(P("N"), P("M"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("S"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("P"), 800, new Heading(90), OneWay: false),
            ]).Snapshot!);
        var service = new TrafficService(store, new IgnoringPublisher(), new ManualTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("N", 180));
        await ReadyAsync(service, Agv2, new Pose("M", 90));

        var result = await service.MoveAgvAsync(Agv1, new PanelId("S"), Start, "transport-1", None);
        var evacuation = await MotionOf(service, Agv2);
        var beforeEvacuated = await MotionOf(service, Agv1);
        await service.HandleMoveCompletedAsync(Agv2, 2, MoveDirection.Forward, new PanelId("P"), None);
        var afterEvacuated = await MotionOf(service, Agv1);

        Assert.Equal(MoveResult.Started, result);
        Assert.Equal<TravelCommand>([new ForwardCommand(800, new PanelId("P"))], evacuation!.Steps);
        Assert.Null(beforeEvacuated);
        Assert.NotNull(afterEvacuated);
    }

    [Fact]
    public async Task どかす動きを止めている間は塞いでいるAGVを動かさず戻したらどかす()
    {
        // N →(180度) M →(180度) S の通路の M に、行き先のない AGV-2 がいる。M の東（90度）に待機パネル P
        var store = new InMemoryMapStore();
        static PanelId P(string id) => new(id);
        store.Publish(MapSnapshot.Create(
            [
                new Panel(P("N"), PanelType.Normal, false),
                new Panel(P("M"), PanelType.Normal, false),
                new Panel(P("S"), PanelType.Normal, false),
                new Panel(P("P"), PanelType.Standby, false),
            ],
            [
                new Connection(P("N"), P("M"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("S"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("P"), 800, new Heading(90), OneWay: false),
            ]).Snapshot!);
        var service = new TrafficService(store, new IgnoringPublisher(), new ManualTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("N", 180));
        await ReadyAsync(service, Agv2, new Pose("M", 90));
        service.SetEvacuationEnabled(false);

        var result = await service.MoveAgvAsync(Agv1, new PanelId("S"), Start, "transport-1", None);
        var whileDisabled = await MotionOf(service, Agv2);
        service.SetEvacuationEnabled(true);
        var afterEnabled = await MotionOf(service, Agv2);

        Assert.Equal(MoveResult.Waiting, result);
        Assert.Null(whileDisabled);
        Assert.Equal<TravelCommand>([new ForwardCommand(800, new PanelId("P"))], afterEnabled!.Steps);
    }
}
