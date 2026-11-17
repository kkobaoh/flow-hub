namespace FlowHub.BuildingBlocks;

/// <summary>
/// コンテキストの間でやり取りするイベント。公開する側のコンテキストの公開インターフェースに、過去形の名前の record で定義する。
/// </summary>
public interface IIntegrationEvent
{
    EventMetadata Metadata { get; }
}

/// <summary>
/// イベントのメタデータ。ログやトレースで、一連の流れと因果をたどるために使う。
/// </summary>
/// <param name="EventId">そのイベント自身の ID。</param>
/// <param name="CorrelationId">一連の流れの ID（例：搬送 ID）。</param>
/// <param name="CausationId">直接のきっかけになったイベントの ID。流れの始まりなら null。</param>
/// <param name="OccurredAt">起きた時刻。</param>
public sealed record EventMetadata(Guid EventId, string CorrelationId, Guid? CausationId, DateTimeOffset OccurredAt)
{
    /// <summary>
    /// 流れの始まりのイベントのメタデータを作る。
    /// </summary>
    public static EventMetadata Start(string correlationId, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new EventMetadata(Guid.NewGuid(), correlationId, null, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// きっかけのイベントから、同じ流れの次のイベントのメタデータを作る。
    /// </summary>
    public static EventMetadata CausedBy(EventMetadata cause, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(cause);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new EventMetadata(Guid.NewGuid(), cause.CorrelationId, cause.EventId, timeProvider.GetUtcNow());
    }
}

/// <summary>
/// イベントの購読者。C# の event（デリゲート）は使わず、このインターフェースを実装したクラスで購読する。
/// 同じイベントが2回届いても結果が変わらないように（冪等に）作る。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "C# の event（デリゲート）の代わりに使う購読者のインターフェース。名前は CLAUDE.md で決めたもの")]
public interface IEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// イベントを公開する。公開できるのは Application の層だけ。
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}
