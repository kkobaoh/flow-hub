using FlowHub.BuildingBlocks;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;

namespace FlowHub.Consumables.Tests;

public class ConsumablesSubscriptionsTests
{
    private sealed class NoDispatching : IDispatchingService
    {
        public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStations : IStationGateway
    {
        public List<TransportId> Grants { get; } = [];

        public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken)
        {
            Grants.Add(transport);
            return Task.CompletedTask;
        }

        public Task RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoDevices : IDeviceGateway
    {
        public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task 配車の搬送の許可の依頼を購読して許可を出す()
    {
        var stations = new RecordingStations();
        using var service = new ConsumablesService(new NoDispatching(), new NoDevices(), stations, new IgnoringReplenishment(), new IgnoringPublisher(), new FixedTimeProvider());
        service.RegisterStation(new StationLayout(new StationId("S1"), new Map.PanelId("S")));
        var subscriptions = new EventSubscriptions();
        ConsumablesSubscriptions.Register(subscriptions, service);
        var bus = new InProcessEventBus(subscriptions);

        await bus.PublishAsync(
            new TransferPermissionRequested(
                EventMetadata.Start("test", new FixedTimeProvider()), new TransportId(1), ConsumablesService.Requester, ["C-1"], "station:S1", TransferSide.Load),
            CancellationToken.None);
        await bus.FlushAsync(CancellationToken.None);

        Assert.Equal([new TransportId(1)], stations.Grants);
        Assert.Equal([typeof(TransferPermissionRequested)], subscriptions.SubscribedEventTypes);
    }
}
