using FlowHub.BuildingBlocks;
using FlowHub.Dispatching;
using FlowHub.Notifications;
using FlowHub.Traffic;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api;

/// <summary>
/// 組み立て（FlowHub.Api）が購読するイベントの一覧。購読は、ここにだけ明示的に書く。
/// </summary>
/// <remarks>
/// 「ログに残して画面で知らせる」と決まっているイベントを、ログに残し、画面のメッセージ（<see cref="INoticeBoard"/>）に残す。
/// 画面へ送るのは、処理のあとの後始末（FlowHubSystem.AfterWorkAsync）で、イベントを届けたあとに行う。
/// </remarks>
internal static class ApiSubscriptions
{
    public static void Register(EventSubscriptions subscriptions, ILoggerFactory loggerFactory, INoticeBoard notices, Func<CancellationToken, Task> reloadFleet)
    {
        ArgumentNullException.ThrowIfNull(reloadFleet);
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(notices);

        // 配車から
        subscriptions.Subscribe(() => new TransportInterruptedNotifier(loggerFactory.CreateLogger<TransportInterruptedNotifier>(), notices));
        subscriptions.Subscribe(() => new MoveBlockedNotifier(loggerFactory.CreateLogger<MoveBlockedNotifier>(), notices));
        subscriptions.Subscribe(() => new NoChargingPortAvailableNotifier(loggerFactory.CreateLogger<NoChargingPortAvailableNotifier>(), notices));
        subscriptions.Subscribe(() => new NoParkingSpaceNotifier(loggerFactory.CreateLogger<NoParkingSpaceNotifier>(), notices));

        // 交通整理から
        subscriptions.Subscribe(() => new ManualStepsDroppedNotifier(loggerFactory.CreateLogger<ManualStepsDroppedNotifier>(), notices));
        subscriptions.Subscribe(() => new AgvAutoRegisteredNotifier(loggerFactory.CreateLogger<AgvAutoRegisteredNotifier>(), notices, reloadFleet));
    }
}

/// <summary>搬送を中断した（AGV の異常、または経路が見つからない）。</summary>
internal sealed partial class TransportInterruptedNotifier(ILogger<TransportInterruptedNotifier> logger, INoticeBoard notices) : IEventSubscriber<TransportInterrupted>
{
    public Task HandleAsync(TransportInterrupted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Transport.ToString(), integrationEvent.Requester, m.EventId, m.CorrelationId, m.CausationId);
        return notices.PostAsync(
            "搬送を中断しました",
            $"搬送 #{integrationEvent.Transport} を、AGV の異常か、経路が見つからないために中断しました。位置リセットのあと、搬送の一覧でやり直すか取りやめるかを選んでください。",
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "搬送 {Transport}（要求元 {Requester}）を中断しました。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string transport, string requester, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>AGV を動かそうとしたが、予約が取れずに止まっている。</summary>
internal sealed partial class MoveBlockedNotifier(ILogger<MoveBlockedNotifier> logger, INoticeBoard notices) : IEventSubscriber<MoveBlocked>
{
    public Task HandleAsync(MoveBlocked integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), integrationEvent.Destination.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return notices.PostAsync(
            "予約が取れずに止まっています",
            $"{integrationEvent.Agv} が、パネル {integrationEvent.Destination} へ向かう予約を待っています。",
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} はパネル {Destination} へ向かう予約が取れず、止まっています。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, string destination, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>行ける充電ポートがない。</summary>
internal sealed partial class NoChargingPortAvailableNotifier(ILogger<NoChargingPortAvailableNotifier> logger, INoticeBoard notices) : IEventSubscriber<NoChargingPortAvailable>
{
    public Task HandleAsync(NoChargingPortAvailable integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return notices.PostAsync(
            "行ける充電ポートがありません",
            $"{integrationEvent.Agv} は、充電ポートが空いたら向かいます。",
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} が行ける充電ポートがありません。空いたら向かわせます。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>未登録の AGV が初めて GET してきたので、種類を決めないまま登録した。画面で知らせ、AGV の登録の様子を読み直す。</summary>
internal sealed partial class AgvAutoRegisteredNotifier(ILogger<AgvAutoRegisteredNotifier> logger, INoticeBoard notices, Func<CancellationToken, Task> reloadFleet) : IEventSubscriber<AgvAutoRegistered>
{
    public async Task HandleAsync(AgvAutoRegistered integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv, m.EventId, m.CorrelationId, m.CausationId);

        await notices.PostAsync(
            "AGV を登録しました",
            $"{integrationEvent.Agv} から初めて通信があったので、登録しました。AGV と AGV の種類の登録の画面で、種類とキャリアの種類を入れてください。入れるまでは動かしません。",
            cancellationToken).ConfigureAwait(false);
        await reloadFleet(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Agv} から初めて通信があったので、種類を決めないまま登録しました。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, AgvId agv, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>マニュアルで並べた1つずつの動作を、予約が取れずに止めて捨てた。</summary>
internal sealed partial class ManualStepsDroppedNotifier(ILogger<ManualStepsDroppedNotifier> logger, INoticeBoard notices) : IEventSubscriber<ManualStepsDropped>
{
    public Task HandleAsync(ManualStepsDropped integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return notices.PostAsync(
            "マニュアルの動作を途中で止めました",
            $"{integrationEvent.Agv} は、マニュアルで並べた動作の予約が取れなかったので、そこで止めて残りを捨てました。",
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} はマニュアルで並べた動作の予約が取れなかったので、そこで止めて残りを捨てました。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, Guid eventId, string correlationId, Guid? causationId);
}

/// <summary>運転状態を停止にしたとき、AGV を止める待機パネルも受け渡しパネルも空いていなかったので、その場で止めた。</summary>
internal sealed partial class NoParkingSpaceNotifier(ILogger<NoParkingSpaceNotifier> logger, INoticeBoard notices) : IEventSubscriber<NoParkingSpace>
{
    public Task HandleAsync(NoParkingSpace integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var m = integrationEvent.Metadata;
        Log(logger, integrationEvent.Agv.ToString(), m.EventId, m.CorrelationId, m.CausationId);
        return notices.PostAsync(
            "止める場所がなく、その場で止めました",
            $"運転状態を停止にしたとき、{integrationEvent.Agv} を止める待機パネルも受け渡しパネルも空いていなかったので、その場で止めました。",
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Agv} を止める待機パネルも受け渡しパネルも空いていないので、その場で止めました。EventId={EventId} CorrelationId={CorrelationId} CausationId={CausationId}")]
    private static partial void Log(ILogger logger, string agv, Guid eventId, string correlationId, Guid? causationId);
}
