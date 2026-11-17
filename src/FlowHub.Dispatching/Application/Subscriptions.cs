using FlowHub.BuildingBlocks;
using FlowHub.Traffic;

namespace FlowHub.Dispatching.Application;

/// <summary>
/// 配車が購読するイベントの一覧。購読は、ここにだけ明示的に書く。
/// </summary>
public static class DispatchingSubscriptions
{
    public static void Register(EventSubscriptions subscriptions, DispatchingService service)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(service);

        // 交通整理から
        subscriptions.Subscribe(() => new AgvArrivedSubscriber(service));
        subscriptions.Subscribe(() => new CarrierLoweredSubscriber(service));
        subscriptions.Subscribe(() => new CarrierRaisedSubscriber(service));
        subscriptions.Subscribe(() => new AgvStoppedSubscriber(service));
        subscriptions.Subscribe(() => new AgvResumedSubscriber(service));
        subscriptions.Subscribe(() => new AgvFaultedSubscriber(service));
        subscriptions.Subscribe(() => new AgvPositionResetCompletedSubscriber(service));
        subscriptions.Subscribe(() => new AgvBatteryChangedSubscriber(service));
    }
}

internal sealed class AgvArrivedSubscriber(DispatchingService service) : IEventSubscriber<AgvArrived>
{
    public Task HandleAsync(AgvArrived integrationEvent, CancellationToken cancellationToken) =>
        service.OnAgvArrivedAsync(integrationEvent, cancellationToken);
}

internal sealed class CarrierLoweredSubscriber(DispatchingService service) : IEventSubscriber<CarrierLowered>
{
    public Task HandleAsync(CarrierLowered integrationEvent, CancellationToken cancellationToken) =>
        service.OnCarrierLoweredAsync(integrationEvent, cancellationToken);
}

internal sealed class CarrierRaisedSubscriber(DispatchingService service) : IEventSubscriber<CarrierRaised>
{
    public Task HandleAsync(CarrierRaised integrationEvent, CancellationToken cancellationToken) =>
        service.OnCarrierRaisedAsync(integrationEvent, cancellationToken);
}

internal sealed class AgvStoppedSubscriber(DispatchingService service) : IEventSubscriber<AgvStopped>
{
    public Task HandleAsync(AgvStopped integrationEvent, CancellationToken cancellationToken) =>
        service.OnAgvStoppedAsync(integrationEvent, cancellationToken);
}

internal sealed class AgvResumedSubscriber(DispatchingService service) : IEventSubscriber<AgvResumed>
{
    public Task HandleAsync(AgvResumed integrationEvent, CancellationToken cancellationToken) =>
        service.OnAgvResumedAsync(integrationEvent, cancellationToken);
}

internal sealed class AgvFaultedSubscriber(DispatchingService service) : IEventSubscriber<AgvFaulted>
{
    public Task HandleAsync(AgvFaulted integrationEvent, CancellationToken cancellationToken) =>
        service.OnAgvFaultedAsync(integrationEvent, cancellationToken);
}

internal sealed class AgvPositionResetCompletedSubscriber(DispatchingService service) : IEventSubscriber<AgvPositionResetCompleted>
{
    public Task HandleAsync(AgvPositionResetCompleted integrationEvent, CancellationToken cancellationToken) =>
        service.OnAgvPositionResetCompletedAsync(integrationEvent, cancellationToken);
}

internal sealed class AgvBatteryChangedSubscriber(DispatchingService service) : IEventSubscriber<AgvBatteryChanged>
{
    public Task HandleAsync(AgvBatteryChanged integrationEvent, CancellationToken cancellationToken) =>
        service.OnAgvBatteryChangedAsync(integrationEvent, cancellationToken);
}
