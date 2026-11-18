using FlowHub.BuildingBlocks;

namespace FlowHub.Consumables.Tests;

/// <summary>補充の頼みを無視する。</summary>
internal sealed class IgnoringReplenishment : IReplenishmentGateway
{
    public Task RequestReplenishmentAsync(VarietyId variety, int quantity, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>公開したイベントを捨てる。</summary>
internal sealed class IgnoringPublisher : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent => Task.CompletedTask;
}
