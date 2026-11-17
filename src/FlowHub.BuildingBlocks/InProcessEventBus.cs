using System.Diagnostics;

namespace FlowHub.BuildingBlocks;

/// <summary>
/// 購読の一覧。コンテキストごとの Subscriptions.cs から、明示的に登録する（DI の自動スキャンは使わない）。
/// </summary>
public sealed class EventSubscriptions
{
    private readonly List<(Type EventType, Func<SubscriberEntry> Create)> _handlers = [];

    /// <summary>
    /// イベントの購読を登録する。届けるときに、そのつどハンドラーを作る。
    /// </summary>
    public void Subscribe<TEvent>(Func<IEventSubscriber<TEvent>> createHandler)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(createHandler);

        _handlers.Add((typeof(TEvent), () =>
        {
            var handler = createHandler();
            return new SubscriberEntry(
                handler.GetType().Name,
                (integrationEvent, cancellationToken) => handler.HandleAsync((TEvent)integrationEvent, cancellationToken));
        }));
    }

    internal IEnumerable<SubscriberEntry> HandlersFor(Type eventType) =>
        _handlers.Where(entry => entry.EventType == eventType).Select(entry => entry.Create());

    internal sealed record SubscriberEntry(string Name, Func<IIntegrationEvent, CancellationToken, Task> HandleAsync);
}

/// <summary>
/// プロセスの中でイベントを届ける。公開したイベントはためておき、<see cref="FlushAsync"/> で届ける。
/// 処理（DB のコミットなど）が終わったあとに FlushAsync を呼ぶことで、「コミット後に届ける」形にする。
/// </summary>
/// <remarks>
/// プロセスが落ちると、まだ届けていないイベントは失われる。必要になったら、失われないアウトボックス方式に差し替える。
/// </remarks>
public sealed class InProcessEventBus(EventSubscriptions subscriptions) : IEventPublisher
{
    /// <summary>イベントを届けるときのトレースの発生元。</summary>
    public static readonly ActivitySource ActivitySource = new("FlowHub.Events");

    private readonly Queue<IIntegrationEvent> _pending = new();

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _pending.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ためておいたイベントを、公開した順に、購読者へ登録した順に届ける。
    /// 失敗したハンドラーがあっても、ほかのハンドラーには届けてから、まとめて失敗を返す。
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        while (_pending.TryDequeue(out var integrationEvent))
        {
            foreach (var handler in subscriptions.HandlersFor(integrationEvent.GetType()))
            {
                using var activity = StartActivity(integrationEvent, handler.Name);

                // 失敗しても次のハンドラーに届けるため、例外を投げない待ち方で待ち、あとで失敗を調べて集める
                var delivery = DeliverAsync(handler, integrationEvent, cancellationToken);
                await delivery.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                if (FailureOf(delivery) is { } failure)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
                    failures.Add(failure);
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("イベントを届けるときに、失敗したハンドラーがあります。", failures);
        }
    }

    /// <summary>
    /// ハンドラーに届ける。ハンドラーが同期的に例外を投げても、返す Task の失敗として包む。
    /// </summary>
    private static async Task DeliverAsync(
        EventSubscriptions.SubscriberEntry handler,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        await handler.HandleAsync(integrationEvent, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 届けた結果の失敗。失敗していなければ null。
    /// </summary>
    private static Exception? FailureOf(Task delivery) =>
        delivery.IsFaulted ? delivery.Exception.InnerException
        : delivery.IsCanceled ? new TaskCanceledException(delivery)
        : null;

    private static Activity? StartActivity(IIntegrationEvent integrationEvent, string handlerName)
    {
        var activity = ActivitySource.StartActivity($"{integrationEvent.GetType().Name} -> {handlerName}");
        activity?.SetTag("flowhub.event_id", integrationEvent.Metadata.EventId);
        activity?.SetTag("flowhub.correlation_id", integrationEvent.Metadata.CorrelationId);
        activity?.SetTag("flowhub.causation_id", integrationEvent.Metadata.CausationId);
        return activity;
    }
}
