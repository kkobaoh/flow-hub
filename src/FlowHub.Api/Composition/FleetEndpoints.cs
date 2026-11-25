using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// 画面から AGV の種類を加える・変える・消す HTTP の窓口（AGV と AGV の種類の登録の画面）。
/// </summary>
/// <remarks>
/// 返す形は <c>{ "result": "…" }</c>。
/// - 加える（<c>POST /agv-types</c>）：<c>saved</c>、<c>idTaken</c>、<c>nameTaken</c>。
/// - 変える（<c>POST /agv-types/{id}/edit</c>）：<c>saved</c>、<c>nameTaken</c>、<c>notFound</c>。
/// - 消す（<c>POST /agv-types/{id}/delete</c>）：<c>deleted</c>、<c>inUse</c>、<c>notFound</c>。
/// - どれも、片方の DB に書けなかったときは <c>failed</c>（先に書いた方を元に戻した）。断ったときは、何も変えていない。
/// - 頼みの形が正しくない（ID の文字、速さが 1 未満、残量の範囲など）：400。
/// 今はログインなしで開く（docs/backlog.md #23）。
/// </remarks>
internal static partial class FleetEndpoints
{
    public static void MapFleetEndpoints(this IEndpointRouteBuilder endpoints, FleetCatalog catalog, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        endpoints.MapPost("/agv-types", (TypeBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV の種類を加える", body.Id ?? "", () => catalog.AddTypeAsync(body.ToInput(body.Id), cancellationToken)));

        endpoints.MapPost("/agv-types/{id}/edit", (string id, TypeBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV の種類を変える", id, () => catalog.UpdateTypeAsync(body.ToInput(id), cancellationToken)));

        endpoints.MapPost("/agv-types/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV の種類を消す", id, () => catalog.DeleteTypeAsync(id, cancellationToken)));
    }

    private static async Task<IResult> RunAsync(ILogger logger, string operation, string target, Func<Task<AgvTypeOperationResult>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            LogDone(logger, operation, target, result);
            return Results.Json(new OperationResponse(ResultText(result)));
        }
        catch (ArgumentException exception)
        {
            // 頼みの形が正しくない。書く前に確かめるので、何も変えていない
            LogRefused(logger, operation, target, exception.Message);
            return Results.BadRequest(new OperationResponse("invalid"));
        }
    }

    private static string ResultText(AgvTypeOperationResult result) => result switch
    {
        AgvTypeOperationResult.Saved => "saved",
        AgvTypeOperationResult.Deleted => "deleted",
        AgvTypeOperationResult.IdTaken => "idTaken",
        AgvTypeOperationResult.NameTaken => "nameTaken",
        AgvTypeOperationResult.NotFound => "notFound",
        AgvTypeOperationResult.InUse => "inUse",
        AgvTypeOperationResult.Failed => "failed",
        _ => throw new InvalidOperationException($"知らない AGV の種類の操作の結果です（{result}）。"),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）の結果：{Result}")]
    private static partial void LogDone(ILogger logger, string operation, string target, AgvTypeOperationResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）を受け付けませんでした：{Reason}")]
    private static partial void LogRefused(ILogger logger, string operation, string target, string reason);

    private sealed record OperationResponse(string Result);

    private sealed record TypeBody(string? Id, string? Name, double? Forward, double? Backward, double? Rotation, int? ChargeAt, int? ResumeAt, bool? Special)
    {
        public AgvTypeInput ToInput(string? id) => new(
            string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("種類の ID を入れてください。", nameof(id)) : id,
            Name ?? "",
            Forward ?? 0,
            Backward ?? 0,
            Rotation ?? 0,
            ChargeAt,
            ResumeAt,
            Special ?? false);
    }
}
