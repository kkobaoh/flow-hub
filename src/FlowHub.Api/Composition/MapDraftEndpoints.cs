using FlowHub.Map;
using FlowHub.Map.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// マップの編集の画面から、マップの下書きを読む・保存する HTTP の窓口。下書きはサーバーに1つだけで、検証しないで保存する。
/// </summary>
/// <remarks>
/// - 読む（<c>GET /map/draft</c>）：<c>{ "revision", "savedAt", "publishedVersion", "draft" }</c>。下書きがなければ、公開中の版の写しを返す（<c>savedAt</c> は null）。公開中の版もなければ <c>draft</c> は null。
/// - 保存する（<c>POST /map/draft</c>、<c>{ "revision": 読んだときの番号, "draft": … }</c>）：<c>{ "result": "saved" | "stale", "revision" }</c>。
///   <c>stale</c> は、読んだあとにほかの人が保存したか、公開して消した（上書きしていない。読み直してもらう）。下書きの形が正しくない：400。
/// 今はログインなしで開く（docs/backlog.md #23）。
/// </remarks>
internal static partial class MapDraftEndpoints
{
    public static void MapMapDraftEndpoints(this IEndpointRouteBuilder endpoints, DbMapStore map, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        endpoints.MapGet("/map/draft", () =>
        {
            var state = map.ReadDraft();
            var draft = state.Saved?.Draft ?? (map.Current is { Drawing: not null } current ? MapDraft.FromSnapshot(current) : null);
            return Results.Json(new DraftResponse(state.Revision, state.Saved?.SavedAt, map.CurrentVersion, draft));
        });

        endpoints.MapPost("/map/draft", (SaveBody body) =>
        {
            if (body.Revision is not { } revision || body.Draft is not { Panels: not null, Connections: not null } draft
                || draft.Panels.Any(p => p is null || p.Id is null || p.Type is null) || draft.Connections.Any(c => c is null || c.From is null || c.To is null))
            {
                LogRefused(logger);
                return Results.BadRequest(new SaveResponse("invalid", 0));
            }

            var saved = map.SaveDraft(revision, draft, timeProvider.GetUtcNow());
            var result = saved.Result switch
            {
                MapDraftSaveResult.Saved => "saved",
                MapDraftSaveResult.Stale => "stale",
                _ => throw new InvalidOperationException($"知らない下書きの保存の結果です（{saved.Result}）。"),
            };
            LogSaved(logger, result, saved.Revision);
            return Results.Json(new SaveResponse(result, saved.Revision));
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からのマップの下書きの保存の結果：{Result}（下書きの番号 {Revision}）")]
    private static partial void LogSaved(ILogger logger, string result, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からのマップの下書きの保存を受け付けませんでした（形が正しくない）")]
    private static partial void LogRefused(ILogger logger);

    private sealed record DraftResponse(int Revision, DateTimeOffset? SavedAt, int PublishedVersion, MapDraft? Draft);

    private sealed record SaveBody(int? Revision, MapDraft? Draft);

    private sealed record SaveResponse(string Result, int Revision);
}
