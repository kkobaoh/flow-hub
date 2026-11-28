using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// 画面から AGV の登録を加える・変える・そろえる・消す HTTP の窓口（AGV と AGV の種類の登録の画面）。
/// </summary>
/// <remarks>
/// 返す形は <c>{ "result": "…" }</c>。
/// - 加える（<c>POST /agv-registrations</c>、<c>{ "id", "type", "carrier" }</c>）：<c>saved</c>、<c>idTaken</c>。
/// - 変える（<c>POST /agv-registrations/{id}/edit</c>、<c>{ "type", "carrier" }</c>）：<c>saved</c>、<c>notFound</c>、<c>notDetached</c>。種類が決まっていない AGV に入れるときも、これを使う。
/// - そろえる（<c>POST /agv-registrations/{id}/align</c>、<c>{ "type" }</c> か <c>{ "carrier" }</c>）：<c>saved</c>、<c>notFound</c>。
/// - 消す（<c>POST /agv-registrations/{id}/delete</c>）：<c>deleted</c>、<c>notFound</c>、<c>notDetached</c>、<c>loaded</c>。
/// - どれも、片方の DB に書けなかったときは <c>failed</c>（先に書いた方を元に戻した）。断ったときは、何も変えていない。
/// - 頼みの形が正しくない（番号が空、知らない種類、選べないキャリアの種類）：400。
/// 画面からの登録操作には構成の変更権限と CSRF トークンが必要。
/// </remarks>
internal static partial class AgvRegistrationEndpoints
{
    public static void MapAgvRegistrationEndpoints(this IEndpointRouteBuilder endpoints, FleetCatalog catalog, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        var registrations = endpoints.MapGroup("/agv-registrations").RequireScreenCommand("configure");
        registrations.MapPost("", (AgvBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV を加える", body.Id ?? "", () => catalog.AddAgvAsync(body.Id ?? "", body.Type ?? "", body.Carrier ?? "", cancellationToken)));

        registrations.MapPost("/{id}/edit", (string id, AgvBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV を変える", id, () => catalog.ChangeAgvAsync(id, body.Type ?? "", body.Carrier ?? "", cancellationToken)));

        registrations.MapPost("/{id}/align", (string id, AgvBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV の登録をそろえる", id, () => catalog.AlignAgvAsync(id, body.Type, body.Carrier, cancellationToken)));

        registrations.MapPost("/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "AGV を消す", id, () => catalog.DeleteAgvAsync(id, cancellationToken)));
    }

    private static async Task<IResult> RunAsync(ILogger logger, string operation, string target, Func<Task<AgvEntryOperationResult>> action)
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

    private static string ResultText(AgvEntryOperationResult result) => result switch
    {
        AgvEntryOperationResult.Saved => "saved",
        AgvEntryOperationResult.Deleted => "deleted",
        AgvEntryOperationResult.IdTaken => "idTaken",
        AgvEntryOperationResult.NotFound => "notFound",
        AgvEntryOperationResult.NotDetached => "notDetached",
        AgvEntryOperationResult.Loaded => "loaded",
        AgvEntryOperationResult.Failed => "failed",
        _ => throw new InvalidOperationException($"知らない AGV の登録の操作の結果です（{result}）。"),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）の結果：{Result}")]
    private static partial void LogDone(ILogger logger, string operation, string target, AgvEntryOperationResult result);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）を受け付けませんでした：{Reason}")]
    private static partial void LogRefused(ILogger logger, string operation, string target, string reason);

    private sealed record OperationResponse(string Result);

    private sealed record AgvBody(string? Id, string? Type, string? Carrier);
}
