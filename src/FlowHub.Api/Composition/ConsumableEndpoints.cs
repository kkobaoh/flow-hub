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
/// - 断ったときは、何も変えていない。頼みの形が正しくない（知らない品種、負の個数など）：400。知らない装置：404。
/// 今はログインなしで開く（docs/backlog.md #23）。
/// </remarks>
internal static partial class ConsumableEndpoints
{
    public static void MapConsumableEndpoints(this IEndpointRouteBuilder endpoints, ConsumablesService consumables, ToolPlanningService toolPlanning, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(consumables);
        ArgumentNullException.ThrowIfNull(toolPlanning);
        ArgumentNullException.ThrowIfNull(logger);

        endpoints.MapPost("/devices/{deviceId}/stocker-setting", (string deviceId, StockerSettingBody body, CancellationToken cancellationToken) =>
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

        endpoints.MapPost("/consumables/{id}/ship", (string id, CancellationToken cancellationToken) =>
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

        endpoints.MapPost("/consumables/{id}/correct", (string id, CorrectionBody body, CancellationToken cancellationToken) =>
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
}
