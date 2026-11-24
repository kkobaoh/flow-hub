using FlowHub.BuildingBlocks;
using FlowHub.Consumables;
using FlowHub.Dispatching;

namespace FlowHub.ToolPlanning.Application;

/// <summary>
/// 消耗品の搬送計画が購読するイベントの一覧。購読は、ここにだけ明示的に書く。
/// </summary>
public static class ToolPlanningSubscriptions
{
    public static void Register(EventSubscriptions subscriptions, ToolPlanningService service)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(service);

        // 消耗品の管理から
        subscriptions.Subscribe(() => new StockChangedSubscriber(service));

        // 配車から
        subscriptions.Subscribe(() => new TransportCompletedSubscriber(service));
        subscriptions.Subscribe(() => new OperationStartedSubscriber(service));
        subscriptions.Subscribe(() => new TransportCanceledSubscriber(service));
    }
}

internal sealed class StockChangedSubscriber(ToolPlanningService service) : IEventSubscriber<StockChanged>
{
    public Task HandleAsync(StockChanged integrationEvent, CancellationToken cancellationToken) =>
        service.ReplanAsync(cancellationToken);
}

internal sealed class TransportCompletedSubscriber(ToolPlanningService service) : IEventSubscriber<TransportCompleted>
{
    public Task HandleAsync(TransportCompleted integrationEvent, CancellationToken cancellationToken) =>
        service.OnTransportCompletedAsync(integrationEvent, cancellationToken);
}

internal sealed class OperationStartedSubscriber(ToolPlanningService service) : IEventSubscriber<OperationStarted>
{
    public Task HandleAsync(OperationStarted integrationEvent, CancellationToken cancellationToken) =>
        service.ReplanAsync(cancellationToken);
}

internal sealed class TransportCanceledSubscriber(ToolPlanningService service) : IEventSubscriber<TransportCanceled>
{
    public Task HandleAsync(TransportCanceled integrationEvent, CancellationToken cancellationToken) =>
        service.OnTransportCanceledAsync(integrationEvent, cancellationToken);
}
