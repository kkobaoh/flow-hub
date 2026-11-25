using System.Globalization;
using System.Text.Json.Nodes;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FlowHub.Traffic.Infrastructure;

/// <summary>
/// AGV との通信の HTTP の窓口（docs/agv-protocol.md）。AGV がコマンドと位置を GET で取りに来て、通知を POST で送ってくる。
/// </summary>
public static class AgvEndpoints
{
    private const string JsonContentType = "application/json";

    /// <summary>
    /// AGV との通信の窓口を登録する。
    /// </summary>
    public static RouteGroupBuilder MapAgvEndpoints(
        this IEndpointRouteBuilder endpoints, TrafficService traffic, AgvEndpointOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(traffic);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var group = endpoints.MapGroup("/agv/{agvId}");
        group.MapGet("/commands", (string agvId, string? lastReceived, CancellationToken cancellationToken) =>
            GetCommandsAsync(traffic, options, timeProvider, agvId, lastReceived, cancellationToken));
        group.MapGet("/position", (string agvId, CancellationToken cancellationToken) =>
            GetPositionAsync(traffic, agvId, cancellationToken));
        group.MapPost("/notifications", (string agvId, HttpRequest request, CancellationToken cancellationToken) =>
            PostNotificationAsync(traffic, agvId, request, cancellationToken));
        return group;
    }

    /// <summary>
    /// コマンドを取りに来た（ロングポーリング）。渡すコマンドがあればすぐに返し、なければできるまで待つ。待つ時間を過ぎたら空の配列を返す。
    /// </summary>
    private static async Task<IResult> GetCommandsAsync(
        TrafficService traffic, AgvEndpointOptions options, TimeProvider timeProvider, string agvId, string? lastReceived, CancellationToken cancellationToken)
    {
        if (!long.TryParse(lastReceived, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var last) || last < -1)
        {
            return Results.BadRequest();
        }

        var id = new AgvId(agvId);
        var deadline = timeProvider.GetUtcNow() + options.CommandWait;
        if (await traffic.ContactAsync(id, cancellationToken).ConfigureAwait(false) == AgvContact.Unconfigured)
        {
            // 種類が決まっていない AGV には、切り離しの AGV と同じく、コマンドを渡さない（待つ時間を過ぎたら空の並び）
            await Task.Delay(options.CommandWait, timeProvider, cancellationToken).ConfigureAwait(false);
            return Json(AgvMessages.Commands([]));
        }

        while (true)
        {
            // 取り逃しのないよう、コマンドを取り出す前に、次の変化を待つ準備をしておく
            var changed = traffic.NextChangeAsync(id);
            var commands = await traffic.HandleCommandRequestAsync(id, last, cancellationToken).ConfigureAwait(false);
            var remaining = deadline - timeProvider.GetUtcNow();
            if (commands.Count > 0 || remaining <= TimeSpan.Zero)
            {
                return Json(AgvMessages.Commands(commands));
            }

            await WaitAsync(changed, remaining, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 状態が変わるか、時間が過ぎるまで待つ。
    /// </summary>
    private static async Task WaitAsync(Task changed, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, timeProvider, delayCancellation.Token);
        await Task.WhenAny(changed, delay).ConfigureAwait(false);
        await delayCancellation.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 位置を取りに来た（再起動したとき）。最後に把握していた位置を返す。わからなければ 204。
    /// </summary>
    private static async Task<IResult> GetPositionAsync(TrafficService traffic, string agvId, CancellationToken cancellationToken)
    {
        var id = new AgvId(agvId);
        if (await traffic.ContactAsync(id, cancellationToken).ConfigureAwait(false) == AgvContact.Unconfigured)
        {
            return Results.NoContent(); // 種類が決まっていない AGV の位置は、わからない
        }

        var position = await traffic.HandlePositionRequestAsync(id, cancellationToken).ConfigureAwait(false);
        return position is null ? Results.NoContent() : Json(AgvMessages.Position(position));
    }

    /// <summary>
    /// 通知（完了、エラー、充電残量）が届いた。形式が読めなければ 400。受け付けても、無視しても 204 を返す。
    /// </summary>
    private static async Task<IResult> PostNotificationAsync(TrafficService traffic, string agvId, HttpRequest request, CancellationToken cancellationToken)
    {
        var id = new AgvId(agvId);
        if (!traffic.IsRegistered(id))
        {
            // 種類が決まっていない AGV の通知は、受けて無視する。知らない AGV の通知では、登録しない
            return traffic.IsUnconfigured(id) ? Results.NoContent() : Results.NotFound();
        }

        using var reader = new StreamReader(request.Body);
        var notification = AgvMessages.ParseNotification(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        if (notification is null)
        {
            return Results.BadRequest();
        }

        await HandleAsync(traffic, id, notification, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Task HandleAsync(TrafficService traffic, AgvId id, AgvNotification notification, CancellationToken cancellationToken) =>
        notification switch
        {
            MotionReceivedNotification n => traffic.HandleMotionReceivedAsync(id, n.Number, cancellationToken),
            MovedNotification n => traffic.HandleMoveCompletedAsync(id, n.Number, n.Direction, n.Panel, cancellationToken),
            RotatedNotification n => traffic.HandleRotationCompletedAsync(id, n.Number, n.Heading, cancellationToken),
            CarrierLoweredNotification n => traffic.HandleCarrierLoweredAsync(id, n.Number, cancellationToken),
            CarrierRaisedNotification n => traffic.HandleCarrierRaisedAsync(id, n.Number, cancellationToken),
            CarrierHeightMeasuredNotification n => traffic.HandleCarrierHeightMeasuredAsync(id, n.Number, n.HeightMm, cancellationToken),
            AlarmClearedNotification n => traffic.HandleAlarmClearedAsync(id, n.Number, cancellationToken),
            PositionResetCompletedNotification n => traffic.HandlePositionResetCompletedAsync(id, n.Number, cancellationToken),
            ErrorNotification n => traffic.HandleAgvErrorAsync(id, n.Kind, cancellationToken),
            BatteryNotification n => traffic.HandleBatteryChangedAsync(id, n.Percent, cancellationToken),
            _ => throw new InvalidOperationException($"未知の通知です：{notification.GetType().Name}"),
        };

    private static IResult Json(JsonObject json) => Results.Content(json.ToJsonString(), JsonContentType);
}
