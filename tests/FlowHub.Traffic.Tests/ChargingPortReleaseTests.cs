using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 充電ポート（充電ピンのあるパネル）の予約を外したら、空いたことを知らせることのテスト。
/// </summary>
/// <remarks>
/// N →(90度) M と、M の東（90度）に充電ピンのある待機パネル C。AGV は C で東を向いている（前進で入ったところ）。
/// </remarks>
public class ChargingPortReleaseTests
{
    private static readonly AgvId Agv = new("AGV-1");
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

    private static async Task<(TrafficService Service, RecordingPublisher Events)> AtChargingPortAsync()
    {
        static PanelId P(string id) => new(id);
        var store = new InMemoryMapStore();
        store.Publish(MapSnapshot.Create(
            [new Panel(P("N"), PanelType.Normal, false), new Panel(P("M"), PanelType.Normal, false), new Panel(P("C"), PanelType.Standby, true)],
            [new Connection(P("N"), P("M"), 800, new Heading(90), OneWay: false), new Connection(P("M"), P("C"), 800, new Heading(90), OneWay: false)]).Snapshot!);
        var events = new RecordingPublisher();
        var service = new TrafficService(store, events, new FixedTimeProvider());
        service.RegisterAgv(Agv, new MotionProfile(500, 250, 90));
        await service.ResetPositionAsync(Agv, new Pose("C", 90), None);
        await service.HandleCommandRequestAsync(Agv, -1, None);
        await service.HandlePositionResetCompletedAsync(Agv, 1, None);
        return (service, events);
    }

    [Fact]
    public async Task 充電ポートを離れて予約を外したら空いたことを知らせる()
    {
        var (service, events) = await AtChargingPortAsync();
        await service.MoveAgvAsync(Agv, new PanelId("N"), null, "move", None); // C → M → N を後退
        var motion = (await service.HandleCommandRequestAsync(Agv, 1, None)).OfType<MotionCommand>().Single();

        await service.HandleMoveCompletedAsync(Agv, motion.Number, MoveDirection.Backward, new PanelId("M"), None);
        var afterLeft = events.Published.OfType<ChargingPortReleased>().ToArray();
        await service.HandleMoveCompletedAsync(Agv, motion.Number, MoveDirection.Backward, new PanelId("N"), None);

        Assert.Equal(new PanelId("C"), Assert.Single(afterLeft).Panel);
        Assert.Single(events.Published.OfType<ChargingPortReleased>()); // 通常パネル M を離れても知らせない
    }

    [Fact]
    public async Task 充電ポートにいるあいだは知らせない()
    {
        var (service, events) = await AtChargingPortAsync();

        await service.MoveAgvAsync(Agv, new PanelId("N"), null, "move", None); // 区間を渡しても、まだ C にいる

        Assert.Empty(events.Published.OfType<ChargingPortReleased>());
    }
}
