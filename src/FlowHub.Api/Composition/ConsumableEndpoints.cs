using FlowHub.Api.Composition.Screens;
using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.ToolPlanning.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// 画面から消耗品を操作する HTTP の窓口（装置とストッカー、ステーションの在庫の画面）。
/// </summary>
/// <remarks>
/// 返す形は <c>{ "result": "…" }</c>。
/// - ストッカーの設定（<c>POST /devices/{deviceId}/stocker-setting</c>）：<c>saved</c>、<c>exceedsSlots</c>（個数の合計がスロットの数を超えている。保存していない）。
/// - 出庫（<c>POST /consumables/{id}/ship</c>）：<c>requested</c>、<c>notAtStation</c>（ステーションにない）、<c>inAssignedTransport</c>（AGV を割り当て済みの、届ける搬送に入っている）。
/// - 要確認を直す（<c>POST /consumables/{id}/correct</c>）：<c>corrected</c>、<c>notNeedingCheck</c>（もうステーションにある要確認の消耗品ではない）。
/// - 指名（<c>POST /consumables/{id}/designate</c>、<c>{ "device": "…" }</c>）：<c>designated</c>、<c>notDesignatable</c>（ステーションにある、要確認でない新品で、予約も出庫の頼みも指名もないものではない）。
/// - 指名の取り消し（<c>POST /consumables/{id}/cancel-designation</c>）：<c>canceled</c>、<c>inAssignedTransport</c>（AGV を割り当て済みの、届ける搬送に入っている）。
/// - 在庫の手入力（<c>POST /consumables</c>（登録）、<c>/consumables/{id}/edit</c>、<c>/consumables/{id}/delete</c>）：<c>saved</c>、<c>alreadyRegistered</c>、<c>notFound</c>、<c>notEditable</c>（AGV の中、加工部・受け渡し部にある、出庫を頼んだもの）、<c>inAssignedTransport</c>、<c>slotTaken</c>。所在は <c>{ "kind": "station", "station": "…" }</c> か <c>{ "kind": "slot", "device": "…", "stocker": 1, "slot": 1 }</c>。
/// - 断ったときは、何も変えていない。頼みの形が正しくない（知らない品種、負の個数、知らない届け先の装置など）：400。ストッカーの設定の知らない装置：404。
/// ストッカー設定と消耗品操作では、ログイン・操作権限・CSRF トークンを確認する。
/// </remarks>
internal static partial class ConsumableEndpoints
{
    public static void MapConsumableEndpoints(this IEndpointRouteBuilder endpoints, ConsumablesService consumables, ToolPlanningService toolPlanning, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(consumables);
        ArgumentNullException.ThrowIfNull(toolPlanning);
        ArgumentNullException.ThrowIfNull(logger);

        var stockerSettings = endpoints.MapGroup("/devices/{deviceId}").RequireScreenCommand("stockerSetting");
        stockerSettings.MapPost("/stocker-setting", (string deviceId, StockerSettingBody body, CancellationToken cancellationToken) =>
        {
            if (consumables.Devices.All(d => d.Value != deviceId))
            {
                return Task.FromResult(Results.NotFound());
            }

            return RunAsync(logger, "ストッカーの設定", deviceId, async () =>
            {
                var counts = (body.Counts ?? throw new ArgumentException("品種ごとの個数を指定してください。", nameof(body)))
                    .ToDictionary(c => ToVariety(c.Variety), c => c.Count ?? throw new ArgumentException("個数を指定してください。", nameof(body)));
                var result = await consumables.SetStockerSettingAsync(new DeviceId(deviceId), counts, cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    StockerSettingResult.Saved => "saved",
                    StockerSettingResult.ExceedsSlots => "exceedsSlots",
                    _ => throw new InvalidOperationException($"知らないストッカーの設定の結果です（{result}）。"),
                };
            });
        });

        var operations = endpoints.MapGroup("/consumables/{id}").RequireScreenCommand("consumableOps");
        operations.MapPost("/ship", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "出庫", id, async () =>
            {
                var result = await toolPlanning.RequestShipmentAsync(ToConsumable(id), cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    ShipmentResult.Requested => "requested",
                    ShipmentResult.NotAtStation => "notAtStation",
                    ShipmentResult.InAssignedTransport => "inAssignedTransport",
                    _ => throw new InvalidOperationException($"知らない出庫の結果です（{result}）。"),
                };
            }));

        operations.MapPost("/correct", (string id, CorrectionBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "要確認の修正", id, async () =>
            {
                var variety = ToVariety(body.Variety);
                var condition = ConditionText.Parse(body.Condition);
                var result = await consumables.CorrectAtStationAsync(ToConsumable(id), variety, condition, cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    CorrectionResult.Corrected => "corrected",
                    CorrectionResult.NotNeedingCheck => "notNeedingCheck",
                    _ => throw new InvalidOperationException($"知らない要確認の修正の結果です（{result}）。"),
                };
            }));

        MapDesignationEndpoints(operations, toolPlanning, logger);
        MapManualEntryEndpoints(endpoints, consumables, toolPlanning, logger);
    }

    private static void MapManualEntryEndpoints(IEndpointRouteBuilder endpoints, ConsumablesService consumables, ToolPlanningService toolPlanning, ILogger logger)
    {
        endpoints.MapPost("/consumables", (RecordBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "在庫の登録", body.Id ?? "", async () =>
            {
                var result = await consumables.RegisterManuallyAsync(ToConsumable(body.Id ?? ""), ToVariety(body.Variety), ConditionText.Parse(body.Condition), ToLocation(body.Location), cancellationToken).ConfigureAwait(false);
                return ManualText(result);
            }));

        endpoints.MapPost("/consumables/{id}/edit", (string id, RecordBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "在庫の修正", id, async () =>
            {
                var result = await toolPlanning.EditManuallyAsync(ToConsumable(id), ToVariety(body.Variety), ConditionText.Parse(body.Condition), ToLocation(body.Location), cancellationToken).ConfigureAwait(false);
                return ManualText(result);
            }));

        endpoints.MapPost("/consumables/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "在庫の削除", id, async () => ManualText(await toolPlanning.DeleteManuallyAsync(ToConsumable(id), cancellationToken).ConfigureAwait(false))));
    }

    private static string ManualText(ManualEntryResult result) => result switch
    {
        ManualEntryResult.Saved => "saved",
        ManualEntryResult.AlreadyRegistered => "alreadyRegistered",
        ManualEntryResult.NotFound => "notFound",
        ManualEntryResult.NotEditable => "notEditable",
        ManualEntryResult.InAssignedTransport => "inAssignedTransport",
        ManualEntryResult.SlotTaken => "slotTaken",
        _ => throw new InvalidOperationException($"知らない在庫の手入力の結果です（{result}）。"),
    };

    /// <summary>画面から届いた所在（ステーションか、ストッカーのスロット）。ほかは頼みの形の誤り。</summary>
    private static ConsumableLocation ToLocation(LocationBody? body) => body?.Kind switch
    {
        "station" when !string.IsNullOrWhiteSpace(body.Station) => new AtStation(new StationId(body.Station)),
        "slot" when !string.IsNullOrWhiteSpace(body.Device) && body.Stocker is { } stocker && body.Slot is { } slot => new InStocker(new DeviceId(body.Device), new SlotRef(stocker, slot)),
        _ => throw new ArgumentException($"所在は、ステーションか、ストッカーのスロットを指定してください（{body?.Kind}）。", nameof(body)),
    };

    private static void MapDesignationEndpoints(IEndpointRouteBuilder endpoints, ToolPlanningService toolPlanning, ILogger logger)
    {
        endpoints.MapPost("/designate", (string id, DesignationBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "指名", id, async () =>
            {
                var device = string.IsNullOrWhiteSpace(body.Device) ? throw new ArgumentException("届け先の装置を指定してください。", nameof(body)) : new DeviceId(body.Device);
                var result = await toolPlanning.DesignateAsync(device, ToConsumable(id), cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    DesignationResult.Designated => "designated",
                    DesignationResult.NotDesignatable => "notDesignatable",
                    _ => throw new InvalidOperationException($"知らない指名の結果です（{result}）。"),
                };
            }));

        endpoints.MapPost("/cancel-designation", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "指名の取り消し", id, async () =>
            {
                var result = await toolPlanning.CancelDesignationAsync(ToConsumable(id), cancellationToken).ConfigureAwait(false);
                return result switch
                {
                    DesignationCancelResult.Canceled => "canceled",
                    DesignationCancelResult.InAssignedTransport => "inAssignedTransport",
                    _ => throw new InvalidOperationException($"知らない指名の取り消しの結果です（{result}）。"),
                };
            }));
    }

    private static VarietyId ToVariety(string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("品種を指定してください。", nameof(value)) : new VarietyId(value);

    private static ConsumableId ToConsumable(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("消耗品の ID を指定してください。", nameof(value)) : new ConsumableId(value);

    private static async Task<IResult> RunAsync(ILogger logger, string operation, string target, Func<Task<string>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            LogDone(logger, operation, target, result);
            return Results.Json(new OperationResponse(result));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // 頼みの形が正しくない（知らない品種、負の個数など）。消耗品の管理は、確かめてから状態を変えるので、何も変えていない
            LogRefused(logger, operation, target, exception.Message);
            return Results.BadRequest(new OperationResponse("invalid"));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）の結果：{Result}")]
    private static partial void LogDone(ILogger logger, string operation, string target, string result);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）を受け付けませんでした：{Reason}")]
    private static partial void LogRefused(ILogger logger, string operation, string target, string reason);

    private sealed record OperationResponse(string Result);

    private sealed record StockerSettingBody(IReadOnlyList<CountBody>? Counts);

    private sealed record CountBody(string? Variety, int? Count);

    private sealed record CorrectionBody(string? Variety, string? Condition);

    private sealed record DesignationBody(string? Device);

    private sealed record RecordBody(string? Id, string? Variety, string? Condition, LocationBody? Location);

    private sealed record LocationBody(string? Kind, string? Station, string? Device, int? Stocker, int? Slot);
}
