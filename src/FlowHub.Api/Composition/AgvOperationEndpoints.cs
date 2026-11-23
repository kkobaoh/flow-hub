using FlowHub.Dispatching.Application;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic;
using FlowHub.Traffic.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// 画面から AGV を操作する HTTP の窓口（<c>/agvs/{agvId}/…</c>）。AGV との通信の窓口（<c>/agv/…</c>）とは別。
/// </summary>
/// <remarks>
/// 返す形は <c>{ "result": "…" }</c>。
/// - 受け付けた：200 と <c>accepted</c>（マニュアルの行き先は <c>alreadyThere</c>、<c>noRoute</c>、<c>blocked</c>、<c>arrivalHeadingNotAllowed</c> もある）。
/// - 位置のパネルをほかの AGV が予約していた：200 と <c>reserved</c>。
/// - 今の状態では受け付けられない（マニュアルでない、走っている途中など）：409 と <c>notAllowed</c>。状態は何も変えていない。
/// - 頼みの形が正しくない（知らないステータス、マップにないパネルなど）：400。知らない AGV：404。
/// 今はログインなしで開く（docs/backlog.md #23）。
/// </remarks>
internal static partial class AgvOperationEndpoints
{
    public static RouteGroupBuilder MapAgvOperationEndpoints(this IEndpointRouteBuilder endpoints, TrafficService traffic, DispatchingService dispatching, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(traffic);
        ArgumentNullException.ThrowIfNull(dispatching);
        ArgumentNullException.ThrowIfNull(logger);

        var group = endpoints.MapGroup("/agvs/{agvId}");
        Task<IResult> Run(string agvId, string operation, Func<AgvId, Task<string>> action) => RunAsync(traffic, logger, agvId, operation, action);

        group.MapPost("/emergency-stop", (string agvId, CancellationToken cancellationToken) =>
            Run(agvId, "緊急停止", async id =>
            {
                await traffic.EmergencyStopAsync(id, cancellationToken).ConfigureAwait(false);
                return Accepted;
            }));

        group.MapPost("/alarm-clear", (string agvId, CancellationToken cancellationToken) =>
            Run(agvId, "アラームクリア", async id =>
            {
                await traffic.ClearAlarmAsync(id, cancellationToken).ConfigureAwait(false);
                return Accepted;
            }));

        group.MapPost("/position-reset", (string agvId, PositionBody body, CancellationToken cancellationToken) =>
            Run(agvId, "位置リセット", async id =>
            {
                var position = body.ToPose() ?? throw new ArgumentException("位置（パネルと向き）を指定してください。", nameof(body));
                return await traffic.ResetPositionAsync(id, position, cancellationToken).ConfigureAwait(false) ? Accepted : Reserved;
            }));

        group.MapPost("/control", (string agvId, ControlBody body, CancellationToken cancellationToken) =>
            Run(agvId, "コントロールステータスの切り替え", async id =>
            {
                var target = ParseControl(body.Status);
                var position = new PositionBody(body.Panel, body.Heading).ToPose();
                return await dispatching.ChangeControlStatusAsync(id, target, position, cancellationToken).ConfigureAwait(false) ? Accepted : Reserved;
            }));

        group.MapPost("/manual/move", (string agvId, ManualMoveBody body, CancellationToken cancellationToken) =>
            Run(agvId, "マニュアルの行き先", async id =>
            {
                var destination = string.IsNullOrWhiteSpace(body.Destination) ? throw new ArgumentException("行き先を指定してください。", nameof(body)) : new PanelId(body.Destination);
                var arrival = body.Arrival is { } degrees ? new Heading(degrees) : (Heading?)null;
                var result = await traffic.ManualMoveAsync(id, destination, arrival, cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    ManualMoveResult.Started => Accepted,
                    ManualMoveResult.AlreadyThere => "alreadyThere",
                    ManualMoveResult.NoRoute => "noRoute",
                    ManualMoveResult.Blocked => "blocked",
                    ManualMoveResult.ArrivalHeadingNotAllowed => "arrivalHeadingNotAllowed",
                    _ => throw new InvalidOperationException($"知らないマニュアルの行き先の結果です（{result}）。"),
                };
            }));

        group.MapPost("/manual/step", (string agvId, ManualStepBody body, CancellationToken cancellationToken) =>
            Run(agvId, "マニュアルの1つずつの動作", async id =>
            {
                var result = await traffic.ManualStepAsync(id, body.ToStep(), cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    ManualStepResult.Accepted => Accepted,
                    ManualStepResult.Blocked => "blocked",
                    ManualStepResult.NotAllowed => NotAllowed,
                    _ => throw new InvalidOperationException($"知らないマニュアルの動作の結果です（{result}）。"),
                };
            }));

        group.MapPost("/manual/carrier", (string agvId, ManualCarrierBody body, CancellationToken cancellationToken) =>
            Run(agvId, "マニュアルのキャリアの上げ下げ", async id =>
            {
                switch (body.Action)
                {
                    case "raise":
                        await traffic.ManualRaiseCarrierAsync(id, cancellationToken).ConfigureAwait(false);
                        break;
                    case "lower":
                        await traffic.ManualLowerCarrierAsync(id, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentException($"知らないキャリアの操作です（{body.Action}）。", nameof(body));
                }

                return Accepted;
            }));

        return group;
    }

    private const string Accepted = "accepted";
    private const string Reserved = "reserved";
    private const string NotAllowed = "notAllowed";

    private static async Task<IResult> RunAsync(TrafficService traffic, ILogger logger, string agvId, string operation, Func<AgvId, Task<string>> action)
    {
        if (string.IsNullOrWhiteSpace(agvId) || traffic.ListAgvs().All(a => a.Id.Value != agvId))
        {
            return Results.NotFound();
        }

        var id = new AgvId(agvId);
        try
        {
            return Results.Json(new OperationResponse(await action(id).ConfigureAwait(false)));
        }
        catch (ArgumentException exception)
        {
            // 頼みの形が正しくない。交通整理と配車は、確かめてから状態を変えるので、何も変えていない
            LogRefused(logger, operation, agvId, exception.Message);
            return Results.BadRequest(new OperationResponse("invalid"));
        }
        catch (InvalidOperationException exception)
        {
            // 今の状態では受け付けられない。交通整理と配車は、確かめてから状態を変えるので、何も変えていない
            LogRefused(logger, operation, agvId, exception.Message);
            return Results.Json(new OperationResponse(NotAllowed), statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static ControlStatus ParseControl(string? status) => status switch
    {
        "auto" => ControlStatus.Auto,
        "assignmentPaused" => ControlStatus.AssignmentPaused,
        "manual" => ControlStatus.Manual,
        "locked" => ControlStatus.Locked,
        "detached" => ControlStatus.Detached,
        _ => throw new ArgumentException($"知らないコントロールステータスです（{status}）。", nameof(status)),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Agv}）を受け付けませんでした：{Reason}")]
    private static partial void LogRefused(ILogger logger, string operation, string agv, string reason);

    /// <summary>操作の結果。</summary>
    private sealed record OperationResponse(string Result);

    /// <summary>位置（パネルと向き）。どちらもないときは「位置なし」、片方だけのときは形の誤り。</summary>
    private sealed record PositionBody(string? Panel, int? Heading)
    {
        public Pose? ToPose() => (Panel, Heading) switch
        {
            (null, null) => null,
            ({ } panel, { } heading) when !string.IsNullOrWhiteSpace(panel) => new Pose(panel, heading),
            _ => throw new ArgumentException("位置は、パネルと向きの両方を指定してください。"),
        };
    }

    private sealed record ControlBody(string? Status, string? Panel, int? Heading);

    private sealed record ManualMoveBody(string? Destination, int? Arrival);

    private sealed record ManualStepBody(string? Kind, int? Heading)
    {
        public ManualStep ToStep() => (Kind, Heading) switch
        {
            ("forward", null) => new ManualForward(),
            ("backward", null) => new ManualBackward(),
            ("rotate", { } heading) => new ManualRotate(heading),
            ("freeRotate", { } heading) => new ManualFreeRotate(heading),
            _ => throw new ArgumentException($"知らない動作か、向きの指定が合いません（{Kind}）。"),
        };
    }

    private sealed record ManualCarrierBody(string? Action);
}
