using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// AGV を止める（搬送の取りやめなどで、まだ渡していないコマンドを取り下げ、受け取ったコマンドを終えたところで止める）ことのテスト。
/// </summary>
/// <remarks>
/// 十字路：N →(180度) M →(180度) S と、E →(270度) M →(270度) W。AGV は N で南（180度）を向いている。
/// W へ向かうと、区間は「M へ前進して 270度へ回転」と「W へ前進」の2つになる。
/// </remarks>
public class AgvStopTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly AgvId Someone = new("SOMEONE");
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string[] Panels = ["N", "M", "S", "E", "W"];

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

    private static async Task<(TrafficService Service, RecordingPublisher Events)> HeadingToWestAsync()
    {
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            Panels.Select(id => new Panel(P(id), PanelType.Normal, false)),
            [
                new Connection(P("N"), P("M"), 800, new Heading(180), OneWay: false),
                new Connection(P("M"), P("S"), 800, new Heading(180), OneWay: false),
                new Connection(P("E"), P("M"), 800, new Heading(270), OneWay: false),
                new Connection(P("M"), P("W"), 800, new Heading(270), OneWay: false),
            ]).Snapshot!);
        var events = new RecordingPublisher();
        var service = new TrafficService(store, events, new FixedTimeProvider());
        service.RegisterAgv(Agv, new MotionProfile(500, 250, 90));
        await service.ResetPositionAsync(Agv, new Pose("N", 180), None);
        await service.HandleCommandRequestAsync(Agv, -1, None);
        await service.HandlePositionResetCompletedAsync(Agv, 1, None);
        await service.MoveAgvAsync(Agv, P("W"), null, "transport-1", None);
        return (service, events);
    }

    private static async Task<MotionCommand?> MotionOf(TrafficService service, long lastReceived) =>
        (await service.HandleCommandRequestAsync(Agv, lastReceived, None)).OfType<MotionCommand>().SingleOrDefault();

    [Fact]
    public async Task 受け取った区間は走り終えたところで止まり残りは走らない()
    {
        var (service, events) = await HeadingToWestAsync();
        var first = await MotionOf(service, 1); // M へ前進して回転（番号 2）

        await service.StopAgvAsync(Agv, "cancel", None);
        await service.HandleMoveCompletedAsync(Agv, first!.Number, MoveDirection.Forward, P("M"), None);
        await service.HandleRotationCompletedAsync(Agv, first.Number, 270, None);

        Assert.Null(await MotionOf(service, first.Number));
        Assert.Equal(new Pose("M", 270), service.GetAgv(Agv).Position);
        Assert.Equal(AgvState.Idle, service.GetAgv(Agv).State);
        Assert.Empty(events.Published.OfType<AgvArrived>());
        Assert.False(service.IsReservedByOther(P("N"), Someone)); // 止まったパネル以外の予約は外す
        Assert.False(service.IsReservedByOther(P("W"), Someone));
    }

    [Fact]
    public async Task まだ渡していない動作コマンドは取り下げてその場で止まる()
    {
        var (service, _) = await HeadingToWestAsync(); // 区間は用意したが、まだ AGV に渡していない

        await service.StopAgvAsync(Agv, "cancel", None);

        Assert.Null(await MotionOf(service, 1));
        Assert.Equal(AgvState.Idle, service.GetAgv(Agv).State);
        Assert.False(service.IsReservedByOther(P("M"), Someone));
        Assert.True(service.IsReservedByOther(P("N"), Someone)); // 今いるパネルは予約したまま
    }

    [Fact]
    public async Task 渡したが受け取っていなかった動作コマンドは次に取りに来たときに取り下げる()
    {
        var (service, _) = await HeadingToWestAsync();
        await MotionOf(service, 1); // 番号 2 を渡した（受け取ったかは、まだわからない）

        await service.StopAgvAsync(Agv, "cancel", None);
        var again = await MotionOf(service, 1); // 受け取っていなかった

        Assert.Null(again);
        Assert.Equal(AgvState.Idle, service.GetAgv(Agv).State);
        Assert.False(service.IsReservedByOther(P("M"), Someone));
    }

    [Fact]
    public async Task システムが動かさないAGVは止められない()
    {
        var (service, _) = await HeadingToWestAsync();
        await service.StopAgvAsync(Agv, "cancel", None);
        await service.ChangeControlStatusAsync(Agv, ControlStatus.Locked, position: null, None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAgvAsync(Agv, "cancel", None));
    }
}
