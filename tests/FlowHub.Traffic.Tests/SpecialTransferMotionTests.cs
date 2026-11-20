using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 特殊消耗品受け渡しパネルでの動き（特殊な前進・後退と回転、キャリアを下ろす角度）のテスト。
/// </summary>
/// <remarks>
/// A →(90度) X →(90度) B。X は特殊消耗品受け渡しパネル（キャリアを下ろす角度は 45度）。AGV は走れる種類。
/// </remarks>
public class SpecialTransferMotionTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly MotionProfile Motion = new(500, 250, 90);
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

    private static PanelId P(string id) => new(id);

    private static async Task<(TrafficService Service, RecordingPublisher Events)> ReadyAsync(Pose start, ControlStatus control = ControlStatus.Auto)
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [
                new Panel(P("A"), PanelType.Normal, false),
                new Panel(P("X"), PanelType.SpecialTransfer, false) { CarrierLoweringHeading = new Heading(45) },
                new Panel(P("B"), PanelType.Normal, false),
            ],
            [
                new Connection(P("A"), P("X"), 800, new Heading(90), OneWay: false),
                new Connection(P("X"), P("B"), 800, new Heading(90), OneWay: false),
            ]).Snapshot!);
        var events = new RecordingPublisher();
        var service = new TrafficService(store, events, new FixedTimeProvider());
        service.RegisterAgv(Agv, Motion, canTravelSpecialTransfer: true);
        await service.ResetPositionAsync(Agv, start, None);
        await service.HandleCommandRequestAsync(Agv, -1, None);
        await service.HandlePositionResetCompletedAsync(Agv, 1, None);
        await service.ChangeControlStatusAsync(Agv, control, position: null, None);
        return (service, events);
    }

    private static async Task<MotionCommand> MotionOf(TrafficService service, long lastReceived = 1) =>
        (await service.HandleCommandRequestAsync(Agv, lastReceived, None)).OfType<MotionCommand>().Single();

    // ---- 自動の動き ----

    [Fact]
    public async Task 搬送で向かうと特殊な前進で入りキャリアを下ろす角度へ特殊な回転で回って止まる()
    {
        var (service, events) = await ReadyAsync(new Pose("A", 90));

        await service.MoveAgvAsync(Agv, P("X"), null, "transport-1", None);
        var motion = await MotionOf(service);
        await service.HandleMoveCompletedAsync(Agv, motion.Number, MoveDirection.Forward, P("X"), None);
        await service.HandleRotationCompletedAsync(Agv, motion.Number, 45, None);

        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("X"), Special: true), new RotateSpecialCommand(45)], motion.Steps);
        Assert.Equal(new Pose("X", 45), service.GetAgv(Agv).Position);
        Assert.Equal(P("X"), Assert.Single(events.Published.OfType<AgvArrived>()).Panel);
    }

    [Fact]
    public async Task 通過するときは出入りを特殊な前進にして回らない()
    {
        var (service, _) = await ReadyAsync(new Pose("A", 90));

        await service.MoveAgvAsync(Agv, P("B"), null, "move", None);

        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("X"), Special: true), new ForwardCommand(800, P("B"), Special: true)], (await MotionOf(service)).Steps);
    }

    [Fact]
    public async Task キャリアを下ろす角度から走り出すときはふつうの回転で回る()
    {
        var (service, _) = await ReadyAsync(new Pose("X", 45));

        await service.MoveAgvAsync(Agv, P("B"), null, "move", None);

        Assert.Equal<TravelCommand>([new RotateCommand(90)], (await MotionOf(service)).Steps);
    }

    [Fact]
    public async Task キャリアを下ろす角度を向いているときだけキャリアを下げられる()
    {
        var (atLoweringHeading, _) = await ReadyAsync(new Pose("X", 45));
        var (atOtherHeading, _) = await ReadyAsync(new Pose("X", 90));

        await atLoweringHeading.LowerCarrierAsync(Agv, "transport-1", None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => atOtherHeading.LowerCarrierAsync(Agv, "transport-1", None));

        Assert.IsType<LowerCarrierCommand>(Assert.Single(await atLoweringHeading.HandleCommandRequestAsync(Agv, 1, None)));
    }

    // ---- マニュアル ----

    [Fact]
    public async Task マニュアルの前進と後退も出入りは特殊な前進と後退を送る()
    {
        var (service, _) = await ReadyAsync(new Pose("A", 90), ControlStatus.Manual);

        await service.ManualStepAsync(Agv, new ManualForward(), None);
        var forward = await MotionOf(service);
        await service.HandleMoveCompletedAsync(Agv, forward.Number, MoveDirection.Forward, P("X"), None);
        await service.ManualStepAsync(Agv, new ManualBackward(), None);
        var backward = await MotionOf(service, forward.Number);

        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("X"), Special: true)], forward.Steps);
        Assert.Equal<TravelCommand>([new BackwardCommand(800, P("A"), Special: true)], backward.Steps);
    }

    [Fact]
    public async Task マニュアルの角度を指定した回転は特殊消耗品受け渡しパネルの上では特殊な回転を送る()
    {
        var (onSpecial, _) = await ReadyAsync(new Pose("X", 90), ControlStatus.Manual);
        var (onNormal, _) = await ReadyAsync(new Pose("A", 90), ControlStatus.Manual);

        await onSpecial.ManualStepAsync(Agv, new ManualFreeRotate(45), None);
        await onNormal.ManualStepAsync(Agv, new ManualFreeRotate(45), None);

        Assert.Equal<TravelCommand>([new RotateSpecialCommand(45)], (await MotionOf(onSpecial)).Steps);
        Assert.Equal<TravelCommand>([new FreeRotateCommand(45)], (await MotionOf(onNormal)).Steps);
    }

    [Fact]
    public async Task マニュアルの回転は特殊消耗品受け渡しパネルの上でもふつうの回転を送る()
    {
        var (service, _) = await ReadyAsync(new Pose("X", 45), ControlStatus.Manual);

        await service.ManualStepAsync(Agv, new ManualRotate(90), None);

        Assert.Equal<TravelCommand>([new RotateCommand(90)], (await MotionOf(service)).Steps);
    }

    [Fact]
    public async Task マニュアルでもキャリアを下ろす角度を向いていなければキャリアを下げられない()
    {
        var (service, _) = await ReadyAsync(new Pose("X", 90), ControlStatus.Manual);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ManualLowerCarrierAsync(Agv, None));
    }

    [Fact]
    public async Task マニュアルで行き先を指定したときもキャリアを下ろす角度へ回って止まる()
    {
        var (service, _) = await ReadyAsync(new Pose("A", 90), ControlStatus.Manual);

        await service.ManualMoveAsync(Agv, P("X"), None);

        Assert.Equal<TravelCommand>([new ForwardCommand(800, P("X"), Special: true), new RotateSpecialCommand(45)], (await MotionOf(service)).Steps);
    }
}
