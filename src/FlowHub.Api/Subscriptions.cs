using FlowHub.BuildingBlocks;
using FlowHub.Dispatching;
using FlowHub.Traffic;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api;

/// <summary>
/// 組み立て（FlowHub.Api）が購読するイベントの一覧。購読は、ここにだけ明示的に書く。
/// </summary>
/// <remarks>
/// 「ログに残して画面で知らせる」と決まっているイベントを、ログに残す。画面に知らせる部分は、画面に取りかかるときに足す。
/// </remarks>
internal static class ApiSubscriptions
{
    public static void Register(EventSubscriptions subscriptions, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // 配車から
        subscriptions.Subscribe(() => new TransportInterruptedLogger(loggerFactory.CreateLogger<TransportInterruptedLogger>()));
        subscriptions.Subscribe(() => new MoveBlockedLogger(loggerFactory.CreateLogger<MoveBlockedLogger>()));
        subscriptions.Subscribe(() => new NoChargingPortAvailableLogger(loggerFactory.CreateLogger<NoChargingPortAvailableLogger>()));

        // 交通整理から
        subscriptions.Subscribe(() => new ManualStepsDroppedLogger(loggerFactory.CreateLogger<ManualStepsDroppedLogger>()));
    }
}

/// <summary>搬送を中断した（AGV の異常、または経路が見つからない）。</summary>
internal sealed partial class TransportInterruptedLogger(ILogger<TransportInterruptedLogger> logger) : IEventSubscriber<TransportInterrupted>
{
    public Task HandleAsync(TransportInterrupted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Transport.ToString(), integrationEvent.Requester, m.EventId, m.CorrelationId, m.CausationId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "搬送 {Transport}（要求元 {Requester}）を中断しました。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string transport, string requester, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>AGV を動かそうとしたが、予約が取れずに止まっている。</summary>
internal sealed partial class MoveBlockedLogger(ILogger<MoveBlockedLogger> logger) : IEventSubscriber<MoveBlocked>
{
    public Task HandleAsync(MoveBlocked integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), integrationEvent.Destination.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} はパネル {Destination} へ向かう予約が取れず、止まっています。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, string destination, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>行ける充電ポートがない。</summary>
internal sealed partial class NoChargingPortAvailableLogger(ILogger<NoChargingPortAvailableLogger> logger) : IEventSubscriber<NoChargingPortAvailable>
{
    public Task HandleAsync(NoChargingPortAvailable integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} が行ける充電ポートがありません。空いたら向かわせます。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>マニュアルで並べた1つずつの動作を、予約が取れずに止めて捨てた。</summary>
internal sealed partial class ManualStepsDroppedLogger(ILogger<ManualStepsDroppedLogger> logger) : IEventSubscriber<ManualStepsDropped>
{
    public Task HandleAsync(ManualStepsDropped integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} はマニュアルで並べた動作の予約が取れなかったので、そこで止めて残りを捨てました。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, Guid eventId, string correlationId, Guid? causationId);
}
