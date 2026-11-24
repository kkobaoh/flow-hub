using FlowHub.BuildingBlocks;
using FlowHub.Dispatching;

namespace FlowHub.Consumables.Application;

/// <summary>
/// 消耗品の管理が購読するイベントの一覧。購読は、ここにだけ明示的に書く。
/// </summary>
public static class ConsumablesSubscriptions
{
    public static void Register(EventSubscriptions subscriptions, ConsumablesService service)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(service);

        // 配車から
        subscriptions.Subscribe(() => new TransferPermissionRequestedSubscriber(service));
    }
}

internal sealed class TransferPermissionRequestedSubscriber(ConsumablesService service) : IEventSubscriber<TransferPermissionRequested>
{
    public Task HandleAsync(TransferPermissionRequested integrationEvent, CancellationToken cancellationToken) =>
        service.OnTransferPermissionRequestedAsync(integrationEvent, cancellationToken);
}
