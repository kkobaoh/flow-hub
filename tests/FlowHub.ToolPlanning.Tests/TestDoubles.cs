using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Dispatching;

namespace FlowHub.ToolPlanning.Tests;

/// <summary>時刻を手で進められる。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// 頼まれた搬送を覚える配車。<see cref="Assigned"/> に入れた搬送は、割り当て済みとして取りやめられない。
/// </summary>
internal sealed class FakeDispatching : IDispatchingService
{
    private long _nextId = 1;

    /// <summary>運転状態がスタートか。</summary>
    public bool IsRunning { get; set; } = true;

    public List<(TransportId Id, TransportOrder Order)> Orders { get; } = [];

    public List<TransportId> Cancelled { get; } = [];

    public HashSet<TransportId> Assigned { get; } = [];

    public int CancelCalls { get; private set; }

    /// <summary>取りやめずに残っている、AGV 自身を搬送元にした頼み（頼んだ順）。</summary>
    public IEnumerable<AgvTransportOrder> ActiveFromAgvOrders => FromAgvOrders.Where(o => !Cancelled.Contains(o.Id)).Select(o => o.Order);

    /// <summary>取りやめずに残っている要求（頼んだ順）。</summary>
    public IEnumerable<TransportOrder> ActiveOrders => Orders.Where(o => !Cancelled.Contains(o.Id)).Select(o => o.Order);

    public Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        var id = new TransportId(_nextId++);
        Orders.Add((id, order));
        return Task.FromResult(id);
    }

    public Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken)
    {
        CancelCalls++;
        if ((!Orders.Any(o => o.Id == id) && !FromAgvOrders.Any(o => o.Id == id)) || Cancelled.Contains(id))
        {
            throw new InvalidOperationException($"搬送 {id} はありません。");
        }

        if (Assigned.Contains(id))
        {
            return Task.FromResult(false);
        }

        Cancelled.Add(id);
        return Task.FromResult(true);
    }

    public Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>AGV 自身を搬送元にした頼み（頼んだ順）。</summary>
    public List<(TransportId Id, AgvTransportOrder Order)> FromAgvOrders { get; } = [];

    public Task<TransportId> RequestTransportFromAgvAsync(AgvTransportOrder order, CancellationToken cancellationToken)
    {
        var id = new TransportId(_nextId++);
        FromAgvOrders.Add((id, order));
        return Task.FromResult(id);
    }

    /// <summary>割り当てたあとでも取りやめるよう頼まれた搬送（頼まれた順）。</summary>
    public List<TransportId> CancelRequests { get; } = [];

    public Task<TransportCancelResult> CancelAsync(TransportId id, CancellationToken cancellationToken)
    {
        CancelRequests.Add(id);
        Cancelled.Add(id);
        return Task.FromResult(TransportCancelResult.Canceled);
    }

    /// <summary>今残っている要求を、すべて割り当て済みにする。</summary>
    public void AssignAll()
    {
        foreach (var id in Orders.Select(o => o.Id).Concat(FromAgvOrders.Select(o => o.Id)).Where(id => !Cancelled.Contains(id)))
        {
            Assigned.Add(id);
        }
    }
}

internal sealed class NoDevices : IDeviceGateway
{
    public Task GrantTransferAsync(DeviceId device, TransportId transport, TransferSide side, IReadOnlyList<CargoSlot> cargo, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class NoStations : IStationGateway
{
    public List<ConsumableId> Shipments { get; } = [];

    public Task GrantTransferAsync(StationId station, TransportId transport, TransferSide side, IReadOnlyList<ConsumableId> cargo, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task RequestShipmentAsync(StationId station, ConsumableId consumable, CancellationToken cancellationToken)
    {
        Shipments.Add(consumable);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingReplenishment : IUpstreamGateway
{
    public List<(VarietyId Variety, int Quantity)> Requests { get; } = [];

    public Task RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken)
    {
        Requests.Add((variety, quantity));
        return Task.CompletedTask;
    }

    public Task NotifySetupErrorAsync(DeviceId device, VarietyId variety, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class IgnoringPublisher : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent => Task.CompletedTask;
}
