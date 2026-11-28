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
/// - 読む（<c>GET /map/draft</c>）：<c>{ "revision", "savedAt", "publishedVersion", "draft", "heights" }</c>。下書きがなければ、公開中の版の写しを返す（<c>savedAt</c> は null）。公開中の版もなければ <c>draft</c> は null。
///   <c>heights</c> は、公開中の版の受け渡しパネルのうち、キャリアの高さの設定があるもの（<c>{ "panel", "heightMm", "source": "measured" | "manual", "setAt" }</c>）。
/// - キャリアの高さを入れる（<c>POST /map/carrier-heights/{panel}</c>、<c>{ "heightMm": 1 以上 | null }</c>）：<c>{ "result": "saved" | "notTransferPanel" | "noMap", "height" }</c>。
///   下書きとは別に、すぐ使う。null なら設定なし（AGV が測る）。高さが 1 未満：400。
/// - 保存する（<c>POST /map/draft</c>、<c>{ "revision": 読んだときの番号, "draft": … }</c>）：<c>{ "result": "saved" | "stale", "revision" }</c>。
///   <c>stale</c> は、読んだあとにほかの人が保存したか、公開して消した（上書きしていない。読み直してもらう）。下書きの形が正しくない：400。
/// - 検証する（<c>POST /map/draft/validate</c>、<c>{ "draft": … }</c>）：<c>{ "problems": [ "…" ] }</c>。公開するときと同じに検証し、見つかった問題を文で返す（保存はしない）。
/// - 公開する（<c>POST /map/draft/publish</c>、<c>{ "revision", "draft" }</c>）：<c>{ "result", "version", "problems", "agvs" }</c>。
///   <c>published</c>（<c>agvs</c> は切り離しにした AGV）、<c>stale</c>、<c>running</c>、<c>agvMoving</c>（<c>agvs</c> は走っている AGV）、<c>problems</c>。
/// キャリアの高さの変更と下書きの保存・検証・公開にはマップ編集権限と CSRF トークンが必要。
/// </remarks>
internal static partial class MapDraftEndpoints
{
    public static void MapMapDraftEndpoints(this IEndpointRouteBuilder endpoints, DbMapStore map, MapPublisher publisher, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        endpoints.MapGet("/map/draft", () =>
        {
            var state = map.ReadDraft();
            var draft = state.Saved?.Draft ?? (map.Current is { Drawing: not null } current ? MapDraft.FromSnapshot(current) : null);
            return Results.Json(new DraftResponse(state.Revision, state.Saved?.SavedAt, map.CurrentVersion, draft, Heights(map)));
        }).RequireAuthorization(UserLoginEndpoints.Ready);

        var edits = endpoints.MapGroup("/map").RequireScreenCommand("configure");
        edits.MapPost("/carrier-heights/{panel}", (string panel, HeightBody body) =>
        {
            if (string.IsNullOrWhiteSpace(panel) || body.HeightMm is <= 0)
            {
                LogRefused(logger);
                return Results.BadRequest(new HeightResponse("invalid", null));
            }

            var id = new PanelId(panel);
            var saved = map.SetManualCarrierHeight(id, body.HeightMm, timeProvider.GetUtcNow());
            var result = saved switch
            {
                CarrierHeightSaveResult.Saved => "saved",
                CarrierHeightSaveResult.NotTransferPanel => "notTransferPanel",
                CarrierHeightSaveResult.NoMap => "noMap",
                _ => throw new InvalidOperationException($"知らないキャリアの高さの結果です（{saved}）。"),
            };
            LogHeightSaved(logger, panel, body.HeightMm, result);
            return Results.Json(new HeightResponse(result, map.CarrierHeightSettingOf(id) is { } setting ? HeightView.From(id, setting) : null));
        });

        var commands = edits.MapGroup("/draft");
        commands.MapPost("/validate", (ValidateBody body) =>
        {
            if (!IsWellFormed(body.Draft, out var draft))
            {
                LogRefused(logger);
                return Results.BadRequest(new ValidateResponse([]));
            }

            var problems = publisher.Validate(draft);
            LogValidated(logger, problems.Count);
            return Results.Json(new ValidateResponse(problems));
        });

        commands.MapPost("/publish", async (SaveBody body, CancellationToken cancellationToken) =>
        {
            if (body.Revision is not { } revision || !IsWellFormed(body.Draft, out var draft))
            {
                LogRefused(logger);
                return Results.BadRequest(new PublishResponse("invalid", 0, [], []));
            }

            var outcome = await publisher.PublishAsync(revision, draft, cancellationToken).ConfigureAwait(false);
            var result = outcome.Result switch
            {
                MapPublishResult.Published => "published",
                MapPublishResult.Stale => "stale",
                MapPublishResult.Running => "running",
                MapPublishResult.AgvMoving => "agvMoving",
                MapPublishResult.Invalid => "problems",
                _ => throw new InvalidOperationException($"知らない公開の結果です（{outcome.Result}）。"),
            };
            return Results.Json(new PublishResponse(result, outcome.Version, outcome.Problems, outcome.Agvs));
        });

        commands.MapPost("", (SaveBody body) =>
        {
            if (body.Revision is not { } revision || !IsWellFormed(body.Draft, out var draft))
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

    /// <summary>公開中の版の受け渡しパネルのうち、キャリアの高さの設定があるもの（並べた順）。</summary>
    private static HeightView[] Heights(DbMapStore map) =>
        map.Current is { } current
            ? [.. current.Panels
                .Where(p => p.Type == PanelType.Transfer)
                .Select(p => (p.Id, Setting: map.CarrierHeightSettingOf(p.Id)))
                .Where(h => h.Setting is not null)
                .Select(h => HeightView.From(h.Id, h.Setting!))]
            : [];

    /// <summary>下書きの形が正しいか（並びと、ID・種類・つながりの元と先の文字がある。値そのものは確かめない）。</summary>
    private static bool IsWellFormed(MapDraft? body, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MapDraft? draft)
    {
        draft = body is { Panels: not null, Connections: not null }
            && body.Panels.All(p => p is { Id: not null, Type: not null })
            && body.Connections.All(c => c is { From: not null, To: not null })
                ? body
                : null;
        return draft is not null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からのキャリアの高さ（{Panel}、{HeightMm} mm。空なら設定なし）の結果：{Result}")]
    private static partial void LogHeightSaved(ILogger logger, string panel, int? heightMm, string result);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からのマップの下書きの検証：問題 {Count} 件")]
    private static partial void LogValidated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からのマップの下書きの保存の結果：{Result}（下書きの番号 {Revision}）")]
    private static partial void LogSaved(ILogger logger, string result, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からのマップの下書きを受け付けませんでした（形が正しくない）")]
    private static partial void LogRefused(ILogger logger);

    private sealed record DraftResponse(int Revision, DateTimeOffset? SavedAt, int PublishedVersion, MapDraft? Draft, IReadOnlyList<HeightView> Heights);

    private sealed record HeightBody(int? HeightMm);

    private sealed record HeightResponse(string Result, HeightView? Height);

    private sealed record HeightView(string Panel, int HeightMm, string Source, DateTimeOffset SetAt)
    {
        public static HeightView From(PanelId panel, CarrierHeightSetting setting) => new(
            panel.Value,
            setting.HeightMm,
            setting.Source switch
            {
                CarrierHeightSource.Measured => "measured",
                CarrierHeightSource.Manual => "manual",
                _ => throw new InvalidOperationException($"知らないキャリアの高さの出どころです（{setting.Source}）。"),
            },
            setting.SetAt);
    }

    private sealed record SaveBody(int? Revision, MapDraft? Draft);

    private sealed record SaveResponse(string Result, int Revision);

    private sealed record ValidateBody(MapDraft? Draft);

    private sealed record ValidateResponse(IReadOnlyList<string> Problems);

    private sealed record PublishResponse(string Result, int Version, IReadOnlyList<string> Problems, IReadOnlyList<string> Agvs);
}
