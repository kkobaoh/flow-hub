using FlowHub.BuildingBlocks;
using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Tests;

public class DispatchingSubscriptionsTests
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly PanelId A = new("A");
    private static readonly PanelId S = new("S");
    private static readonly PanelId D = new("D");

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeTraffic : ITrafficService
    {
        public List<string> Calls { get; } = [];

        public AgvView GetAgv(AgvId id) => new(id, new Pose("A", 0), AgvState.Idle, true, 0);

        public TimeSpan? EstimateMoveCost(AgvId id, PanelId destination) => TimeSpan.FromSeconds(1);

        public bool IsReservedByOther(PanelId panel, AgvId self) => false;

        public Task<MoveResult> MoveAgvAsync(AgvId id, PanelId destination, DateTimeOffset? neededBy, string correlationId, CancellationToken cancellationToken)
        {
            Calls.Add($"move {destination}");
            return Task.FromResult(MoveResult.Started);
        }

        public Task LowerCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken)
        {
            Calls.Add("lower");
            return Task.CompletedTask;
        }

        public Task RaiseCarrierAsync(AgvId id, string correlationId, CancellationToken cancellationToken)
        {
            Calls.Add("raise");
            return Task.CompletedTask;
        }

        public Task<bool> ChangeControlStatusAsync(AgvId id, ControlStatus target, Pose? position, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public void SetEvacuationEnabled(bool enabled)
        {
        }
    }

    private static EventMetadata Meta() => EventMetadata.Start("test", new FixedTimeProvider());

    [Fact]
    public async Task 交通整理のイベントを購読して搬送を進める()
    {
        var map = new InMemoryMapStore();
        map.Publish(MapSnapshot.Create(
            [new Panel(A, PanelType.Normal, false), new Panel(S, PanelType.Transfer, false), new Panel(D, PanelType.Transfer, false)],
            [new Connection(A, S, 800, new Heading(0), false), new Connection(A, D, 800, new Heading(90), false)]).Snapshot!);
        var traffic = new FakeTraffic();
        var subscriptions = new EventSubscriptions();
        var bus = new InProcessEventBus(subscriptions);
        using var service = new DispatchingService(traffic, map, bus, new FixedTimeProvider());
        DispatchingSubscriptions.Register(subscriptions, service);
        service.RegisterAgv(Agv, new CarrierTypeId("X"), 20, 80);
        await service.RequestTransportAsync(
            new TransportOrder("ToolPlanning", ["C-1"], S, "station-1", D, "device-1", new CarrierTypeId("X"), DateTimeOffset.MaxValue),
            CancellationToken.None);

        await bus.PublishAsync(new AgvArrived(Meta(), Agv, S), CancellationToken.None);
        await bus.FlushAsync(CancellationToken.None);

        Assert.Equal(["move S", "lower"], traffic.Calls);
    }

    [Fact]
    public void 交通整理のすべてのイベントを購読する()
    {
        var subscriptions = new EventSubscriptions();
        using var service = new DispatchingService(new FakeTraffic(), new InMemoryMapStore(), new InProcessEventBus(subscriptions), new FixedTimeProvider());

        DispatchingSubscriptions.Register(subscriptions, service);

        Assert.Equal(
            [
                typeof(AgvArrived), typeof(CarrierLowered), typeof(CarrierRaised), typeof(AgvStopped),
                typeof(AgvResumed), typeof(AgvFaulted), typeof(AgvPositionResetCompleted), typeof(AgvBatteryChanged),
                typeof(ChargingPortReleased),
            ],
            subscriptions.SubscribedEventTypes);
    }
}
