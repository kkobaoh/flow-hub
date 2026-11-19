using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// マニュアルのコマンド（行き先の指定と、1つずつの動作）のテスト。
/// </summary>
/// <remarks>
/// 十字路：N →(180度) M →(180度) S と、E →(270度) M →(270度) W。S の東（90度）に受け渡しパネル T（行き止まり）。
/// 別の島に、一方通行で回る Q1 →(90度) Q2 →(0度) Q3 →(270度) Q4 →(180度) Q1。どの接続も 800mm。
/// </remarks>
public class ManualCommandTests
{
    private static readonly AgvId Agv1 = new("AGV-1");
    private static readonly AgvId Agv2 = new("AGV-2");
    private static readonly MotionProfile Motion = new(500, 250, 90);
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] RingPanels = ["A", "B", "C", "D", "E", "H"];
    private static readonly string[] NormalPanels = ["N", "M", "S", "E", "W", "Q1", "Q2", "Q3", "Q4"];

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

    private static PanelId P(string id) => new(id);

    private static InMemoryMapStore Map()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            NormalPanels.Select(id => new Panel(P(id), PanelType.Normal, false)).Append(new Panel(P("T"), PanelType.Transfer, false)),
            [
                new Connection(P("N"), P("M"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("S"), 800, new Heading(180), OneWay: false),
                new Connection(P("E"), P("M"), 800, new Heading(270), OneWay: false),
                new Connection(P("M"), P("W"), 800, new Heading(270), OneWay: false),
                new Connection(P("S"), P("T"), 800, new Heading(90), OneWay: false),
                new Connection(P("Q1"), P("Q2"), 800, new Heading(90), OneWay: true),
                new Connection(P("Q2"), P("Q3"), 800, new Heading(0), OneWay: true),
                new Connection(P("Q3"), P("Q4"), 800, new Heading(270), OneWay: true),
                new Connection(P("Q4"), P("Q1"), 800, new Heading(180), OneWay: true),
            ]).Snapshot!);
        return store;
    }

    private static async Task ReadyAsync(TrafficService service, AgvId agv, Pose pose, ControlStatus status)
    {
        service.RegisterAgv(agv, Motion);
        await service.ResetPositionAsync(agv, pose, None);
        await service.HandleCommandRequestAsync(agv, -1, None);
        await service.HandlePositionResetCompletedAsync(agv, 1, None);
        await service.ChangeControlStatusAsync(agv, status, position: null, None);
    }

    /// <summary>マニュアルの AGV-1 が1台だけ。</summary>
    private static async Task<(TrafficService Service, RecordingPublisher Events)> OneManualAsync(Pose pose)
    {
        var events = new RecordingPublisher();
        var service = new TrafficService(Map(), events, new FixedTimeProvider());
        await ReadyAsync(service, Agv1, pose, ControlStatus.Manual);
        return (service, events);
    }

    /// <summary>マニュアルの AGV-1 と、ステータスを指定した AGV-2。</summary>
    private static async Task<(TrafficService Service, RecordingPublisher Events)> TwoAsync(Pose agv1, Pose agv2, ControlStatus agv2Status)
    {
        var events = new RecordingPublisher();
        var service = new TrafficService(Map(), events, new FixedTimeProvider());
        await ReadyAsync(service, Agv1, agv1, ControlStatus.Manual);
        await ReadyAsync(service, Agv2, agv2, agv2Status);
        return (service, events);
    }

    private static async Task<MotionCommand?> MotionOf(TrafficService service, AgvId agv, long lastReceived = 1) =>
        (await service.HandleCommandRequestAsync(agv, lastReceived, None)).OfType<MotionCommand>().SingleOrDefault();

    // ---- 行き先の指定 ----

    [Fact]
    public async Task マニュアルでないAGVには行き先も1つずつの動作も指定できない()
    {
        var events = new RecordingPublisher();
        var service = new TrafficService(Map(), events, new FixedTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("N", 180), ControlStatus.Auto);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualMoveAsync(Agv1, P("S"), None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualStepAsync(Agv1, new ManualForward(), None));
    }

    [Fact]
    public async Task 行き先を指定すると経路を求めて走らせ着いても着いたことは知らせない()
    {
        var (service, events) = await OneManualAsync(new Pose("N", 180));

        var result = await service.ManualMoveAsync(Agv1, P("S"), None);
        var motion = await MotionOf(service, Agv1);
        await service.HandleMoveCompletedAsync(Agv1, motion!.Number, MoveDirection.Forward, P("M"), None);
        await service.HandleMoveCompletedAsync(Agv1, motion.Number, MoveDirection.Forward, P("S"), None);

        Assert.Equal(ManualMoveResult.Started, result);
        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("M")), new ForwardCommand(800, P("S"))], motion.Steps);
        Assert.Equal(new Pose("S", 180), service.GetAgv(Agv1).Position);
        Assert.Empty(events.Published.OfType<AgvArrived>());
    }

    [Fact]
    public async Task 行き先がほかのAGVに塞がれていたら受け付けず行き先も残さない()
    {
        var (service, _) = await TwoAsync(new Pose("N", 180), new Pose("M", 270), ControlStatus.Locked);

        var result = await service.ManualMoveAsync(Agv1, P("S"), None);
        await service.ChangeControlStatusAsync(Agv2, ControlStatus.Detached, position: null, None); // 塞いでいた AGV がいなくなっても

        Assert.Equal(ManualMoveResult.Blocked, result);
        Assert.Null(await MotionOf(service, Agv1));
        Assert.Equal(AgvState.Idle, service.GetAgv(Agv1).State);
    }

    [Fact]
    public async Task マップの上に経路がなければ受け付けない()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));

        var result = await service.ManualMoveAsync(Agv1, P("Q1"), None); // 別の島

        Assert.Equal(ManualMoveResult.NoRoute, result);
        Assert.Null(await MotionOf(service, Agv1));
    }

    [Fact]
    public async Task 今いるパネルを指定したらそのまま()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));

        Assert.Equal(ManualMoveResult.AlreadyThere, await service.ManualMoveAsync(Agv1, P("N"), None));
    }

    [Fact]
    public async Task マニュアルの行き先は全台の計画で一番先にする()
    {
        // AGV-2（オート）は先に W への搬送を頼まれている（必要時刻は今）が、緊急停止中でまだ走れない
        var (service, _) = await TwoAsync(new Pose("N", 180), new Pose("E", 270), ControlStatus.Auto);
        await service.EmergencyStopAsync(Agv2, None);
        await service.MoveAgvAsync(Agv2, P("W"), Now, "transport", None);

        await service.ManualMoveAsync(Agv1, P("S"), None);

        Assert.NotNull(await MotionOf(service, Agv1)); // 十字路は、マニュアルの AGV-1 が先に通る
    }

    [Fact]
    public async Task 走り出したあとで塞がれたらその場で待つ()
    {
        var (service, _) = await TwoAsync(new Pose("N", 180), new Pose("E", 270), ControlStatus.Auto);
        await service.ManualMoveAsync(Agv1, P("W"), None); // N → M（回転）→ W
        var first = await MotionOf(service, Agv1);
        await service.HandleMoveCompletedAsync(Agv1, first!.Number, MoveDirection.Forward, P("M"), None);

        // 回転を終える前に、W に AGV-2 を置いて塞ぐ（W は計画にないので、AGV-2 は M を通らずに W へ位置リセットできる）
        await service.ChangeControlStatusAsync(Agv2, ControlStatus.Locked, position: null, None);
        await service.ResetPositionAsync(Agv2, new Pose("W", 270), None);
        await service.HandleRotationCompletedAsync(Agv1, first.Number, 270, None);
        var whileBlocked = await MotionOf(service, Agv1, first.Number);
        await service.ChangeControlStatusAsync(Agv2, ControlStatus.Detached, position: null, None);
        var afterCleared = await MotionOf(service, Agv1, first.Number);

        Assert.Null(whileBlocked);
        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("W"))], afterCleared!.Steps); // 行き先を捨てずに待っていた
    }

    [Fact]
    public async Task 走っている途中は別の指示を受け付けない()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));
        await service.ManualMoveAsync(Agv1, P("S"), None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualMoveAsync(Agv1, P("N"), None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualStepAsync(Agv1, new ManualForward(), None));
    }

    [Fact]
    public async Task 行き先へ走っている途中に緊急停止してもアラームクリアのあとに続きを走る()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));
        await service.ManualMoveAsync(Agv1, P("S"), None); // 番号 2
        await service.EmergencyStopAsync(Agv1, None); // 番号 3（番号 2 は受け取る前に取り下げる）

        await service.ClearAlarmAsync(Agv1, None); // 番号 4
        await service.HandleCommandRequestAsync(Agv1, 1, None);
        await service.HandleAlarmClearedAsync(Agv1, 4, None);

        Assert.Equal(P("S"), ((ForwardCommand)(await MotionOf(service, Agv1, 4))!.Steps[^1]).To);
    }

    // ---- 1つずつの動作 ----

    [Fact]
    public async Task 前進と後退は隣のパネルへ1つだけ動かす()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));

        var forward = await service.ManualStepAsync(Agv1, new ManualForward(), None);
        var forwardMotion = await MotionOf(service, Agv1);
        await service.HandleMoveCompletedAsync(Agv1, forwardMotion!.Number, MoveDirection.Forward, P("M"), None);
        var backward = await service.ManualStepAsync(Agv1, new ManualBackward(), None);
        var backwardMotion = await MotionOf(service, Agv1, forwardMotion.Number);

        Assert.Equal(ManualStepResult.Accepted, forward);
        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("M"))], forwardMotion.Steps);
        Assert.Equal(ManualStepResult.Accepted, backward);
        Assert.Equal<TravelCommand>([new BackwardCommand(800, P("N"))], backwardMotion!.Steps);
    }

    [Fact]
    public async Task その向きに隣のパネルがなければ受け付けない()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));

        Assert.Equal(ManualStepResult.NotAllowed, await service.ManualStepAsync(Agv1, new ManualBackward(), None)); // N の北には何もない
        Assert.Null(await MotionOf(service, Agv1));
    }

    [Fact]
    public async Task 一方通行の逆には進めない()
    {
        var (againstFlow, _) = await OneManualAsync(new Pose("Q2", 270)); // Q2 から西（270度）へ進むと Q1 だが、Q1 → Q2 の一方通行
        var (alongFlow, _) = await OneManualAsync(new Pose("Q1", 90));

        Assert.Equal(ManualStepResult.NotAllowed, await againstFlow.ManualStepAsync(Agv1, new ManualForward(), None));
        Assert.Equal(ManualStepResult.Accepted, await alongFlow.ManualStepAsync(Agv1, new ManualForward(), None));
    }

    [Fact]
    public async Task 行き止まりのパネルには後退で入れず前進で出られず回転もできない()
    {
        var (atS, _) = await OneManualAsync(new Pose("S", 270)); // 後退すると東の T へ
        var (facingS, _) = await OneManualAsync(new Pose("T", 270)); // 前進すると西の S へ
        var (atT, _) = await OneManualAsync(new Pose("T", 90)); // 前進で入ったときの向き

        Assert.Equal(ManualStepResult.NotAllowed, await atS.ManualStepAsync(Agv1, new ManualBackward(), None));
        Assert.Equal(ManualStepResult.NotAllowed, await facingS.ManualStepAsync(Agv1, new ManualForward(), None));
        Assert.Equal(ManualStepResult.NotAllowed, await atT.ManualStepAsync(Agv1, new ManualRotate(0), None));
        Assert.Equal(ManualStepResult.NotAllowed, await atT.ManualStepAsync(Agv1, new ManualFreeRotate(45), None));
        Assert.Equal(ManualStepResult.Accepted, await atT.ManualStepAsync(Agv1, new ManualBackward(), None));
    }

    [Theory]
    [InlineData(45)]
    [InlineData(-90)]
    [InlineData(360)]
    public async Task 回転は0と90と180と270度だけ(int heading)
    {
        var (service, _) = await OneManualAsync(new Pose("M", 180));

        Assert.Equal(ManualStepResult.NotAllowed, await service.ManualStepAsync(Agv1, new ManualRotate(heading), None));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(360)]
    public async Task 角度を指定した回転は0から359度だけ(int heading)
    {
        var (service, _) = await OneManualAsync(new Pose("M", 180));

        Assert.Equal(ManualStepResult.NotAllowed, await service.ManualStepAsync(Agv1, new ManualFreeRotate(heading), None));
    }

    [Fact]
    public async Task 角度を指定した回転のあとはラインの向きに合わせるまで前進も後退もできない()
    {
        var (service, _) = await OneManualAsync(new Pose("M", 180));

        await service.ManualStepAsync(Agv1, new ManualFreeRotate(45), None);
        var free = await MotionOf(service, Agv1);
        await service.HandleRotationCompletedAsync(Agv1, free!.Number, 45, None);
        var forwardAtOddAngle = await service.ManualStepAsync(Agv1, new ManualForward(), None);
        var align = await service.ManualStepAsync(Agv1, new ManualRotate(90), None);
        var rotate = await MotionOf(service, Agv1, free.Number);

        Assert.Equal<TravelCommand>([new FreeRotateCommand(45)], free.Steps);
        Assert.Equal(new Pose("M", 45), service.GetAgv(Agv1).Position);
        Assert.Equal(ManualStepResult.NotAllowed, forwardAtOddAngle);
        Assert.Equal(ManualStepResult.Accepted, align);
        Assert.Equal<TravelCommand>([new RotateCommand(90)], rotate!.Steps);
    }

    [Fact]
    public async Task 前の動作が終わる前に送られた動作は順に並べる()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));

        await service.ManualStepAsync(Agv1, new ManualForward(), None);
        var second = await service.ManualStepAsync(Agv1, new ManualForward(), None); // M から S へ
        var first = await MotionOf(service, Agv1);
        var beforeFirstDone = await MotionOf(service, Agv1, first!.Number);
        await service.HandleMoveCompletedAsync(Agv1, first.Number, MoveDirection.Forward, P("M"), None);
        var next = await MotionOf(service, Agv1, first.Number);

        Assert.Equal(ManualStepResult.Accepted, second);
        Assert.Null(beforeFirstDone);
        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("S"))], next!.Steps);
    }

    [Fact]
    public async Task すぐに渡せる動作の予約が取れなければ受け付けない()
    {
        var (service, _) = await TwoAsync(new Pose("N", 180), new Pose("M", 270), ControlStatus.Locked);

        var result = await service.ManualStepAsync(Agv1, new ManualForward(), None);

        Assert.Equal(ManualStepResult.Blocked, result);
        Assert.Null(await MotionOf(service, Agv1));
        Assert.Equal(AgvState.Idle, service.GetAgv(Agv1).State);
    }

    [Fact]
    public async Task 並べた動作を渡すときに予約が取れなければそこで止め残りを捨てて知らせる()
    {
        var (service, events) = await TwoAsync(new Pose("N", 180), new Pose("S", 270), ControlStatus.Locked);
        await service.ManualStepAsync(Agv1, new ManualForward(), None); // N → M
        await service.ManualStepAsync(Agv1, new ManualForward(), None); // M → S（AGV-2 がいる）
        await service.ManualStepAsync(Agv1, new ManualRotate(90), None);
        var first = await MotionOf(service, Agv1);

        await service.HandleMoveCompletedAsync(Agv1, first!.Number, MoveDirection.Forward, P("M"), None);

        Assert.Null(await MotionOf(service, Agv1, first.Number));
        Assert.Equal(AgvState.Idle, service.GetAgv(Agv1).State);
        Assert.Equal(Agv1, Assert.Single(events.Published.OfType<ManualStepsDropped>()).Agv);
    }

    [Fact]
    public async Task ほかのAGVはマニュアルのAGVがいなくなったパネルを通れるように計画し直す()
    {
        // マニュアルの AGV-1 が十字路の M にいて、AGV-2（オート）は E から W へ行けずに待っている
        var (service, _) = await TwoAsync(new Pose("M", 180), new Pose("E", 270), ControlStatus.Auto);
        var waiting = await service.MoveAgvAsync(Agv2, P("W"), null, "move", None);

        await service.ManualStepAsync(Agv1, new ManualForward(), None); // M → S
        var step = await MotionOf(service, Agv1);
        await service.HandleMoveCompletedAsync(Agv1, step!.Number, MoveDirection.Forward, P("S"), None);

        Assert.Equal(MoveResult.Waiting, waiting);
        Assert.NotNull(await MotionOf(service, Agv2));
    }

    /// <summary>
    /// 四角い環：A →(90度) B →(0度) C と、A →(0度) D →(90度) C。B の東（90度）に E。A から北東（45度）へ斜めに H。
    /// </summary>
    private static InMemoryMapStore Ring()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            RingPanels.Select(id => new Panel(P(id), PanelType.Normal, false)),
            [
                new Connection(P("A"), P("B"), 800, new Heading(90), OneWay: false),
                new Connection(P("B"), P("C"), 800, new Heading(0), OneWay: false),
                new Connection(P("A"), P("D"), 800, new Heading(0), OneWay: false),
                new Connection(P("D"), P("C"), 800, new Heading(90), OneWay: false),
                new Connection(P("B"), P("E"), 800, new Heading(90), OneWay: false),
                new Connection(P("A"), P("H"), 1131, new Heading(45), OneWay: false),
            ]).Snapshot!);
        return store;
    }

    [Fact]
    public async Task 斜めの接続があってもラインの向きでなければ前進も後退もできない()
    {
        var service = new TrafficService(Ring(), new RecordingPublisher(), new FixedTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("A", 45), ControlStatus.Manual);

        Assert.Equal(ManualStepResult.NotAllowed, await service.ManualStepAsync(Agv1, new ManualForward(), None));
    }

    [Fact]
    public async Task マニュアルの1つずつの動作は全台の計画の通る順番には従わず予約だけで守る()
    {
        // AGV-2（オート）は緊急停止中で、B を通って C へ行く計画（B の通る順番は AGV-2 が先）。B は予約していない
        var service = new TrafficService(Ring(), new RecordingPublisher(), new FixedTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("E", 270), ControlStatus.Manual);
        await ReadyAsync(service, Agv2, new Pose("A", 90), ControlStatus.Auto);
        await service.EmergencyStopAsync(Agv2, None);
        await service.MoveAgvAsync(Agv2, P("C"), Now, "transport", None);

        var result = await service.ManualStepAsync(Agv1, new ManualForward(), None); // E → B

        Assert.Equal(ManualStepResult.Accepted, result);
        Assert.NotNull(await MotionOf(service, Agv1));
    }

    [Fact]
    public async Task マニュアルの1つずつの動作でパネルを予約したらほかのAGVはそれを避けて計画し直す()
    {
        var service = new TrafficService(Ring(), new RecordingPublisher(), new FixedTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("E", 270), ControlStatus.Manual);
        await ReadyAsync(service, Agv2, new Pose("A", 90), ControlStatus.Auto);
        await service.EmergencyStopAsync(Agv2, None); // 番号 2
        await service.MoveAgvAsync(Agv2, P("C"), Now, "transport", None); // B を通る計画

        await service.ManualStepAsync(Agv1, new ManualForward(), None); // E → B を予約する
        await service.ClearAlarmAsync(Agv2, None); // 番号 3
        await service.HandleCommandRequestAsync(Agv2, 2, None);
        await service.HandleAlarmClearedAsync(Agv2, 3, None);
        var detour = await MotionOf(service, Agv2, 3);

        Assert.Equal<TravelCommand>([new RotateCommand(0)], detour!.Steps); // D を回る（B を通るなら、まず東へ前進する）
    }

    [Fact]
    public async Task 緊急停止中は行き先も1つずつの動作も受け付けない()
    {
        var (service, _) = await OneManualAsync(new Pose("N", 180));
        await service.EmergencyStopAsync(Agv1, None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualMoveAsync(Agv1, P("S"), None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualStepAsync(Agv1, new ManualForward(), None));
    }

    // ---- キャリアの上げ下げ ----

    [Fact]
    public async Task マニュアルのAGVはキャリアを上げ下げできる()
    {
        var (service, _) = await OneManualAsync(new Pose("T", 90));

        await service.ManualLowerCarrierAsync(Agv1, None);
        var lower = Assert.Single(await service.HandleCommandRequestAsync(Agv1, 1, None));
        await service.HandleCarrierLoweredAsync(Agv1, lower.Number, None);
        await service.ManualRaiseCarrierAsync(Agv1, None);
        var raise = Assert.Single(await service.HandleCommandRequestAsync(Agv1, lower.Number, None));

        Assert.IsType<LowerCarrierCommand>(lower);
        Assert.IsType<RaiseCarrierCommand>(raise);
    }

    [Fact]
    public async Task マニュアルでないAGVはマニュアルでキャリアを上げ下げできない()
    {
        var events = new RecordingPublisher();
        var service = new TrafficService(Map(), events, new FixedTimeProvider());
        await ReadyAsync(service, Agv1, new Pose("T", 90), ControlStatus.Auto);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualLowerCarrierAsync(Agv1, None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualRaiseCarrierAsync(Agv1, None));
    }
}
