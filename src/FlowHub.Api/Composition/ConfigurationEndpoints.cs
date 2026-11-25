using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition;

/// <summary>
/// 画面から消耗品の構成（加工の種類、品種、入庫の予定）を加える・変える・消す HTTP の窓口（消耗品の構成の画面）。
/// </summary>
/// <remarks>
/// 返す形は <c>{ "result": "…" }</c>。<c>saved</c>、<c>alreadyRegistered</c>、<c>notFound</c>、<c>inUse</c>。
/// - 加工の種類：<c>POST /configuration/process-types</c>（<c>{ "id", "carrier", "capacity" }</c>）、<c>/configuration/process-types/{id}/edit</c>（<c>{ "carrier", "capacity" }</c>）、<c>/configuration/process-types/{id}/delete</c>。
/// - 品種：<c>POST /configuration/varieties</c>（<c>{ "id", "processType" }</c>）、<c>/configuration/varieties/{id}/edit</c>（<c>{ "processType" }</c>）、<c>/configuration/varieties/{id}/delete</c>。
/// - 入庫の予定：<c>POST /configuration/varieties/{id}/expected-arrivals</c>（<c>{ "consumable" }</c>）、<c>/configuration/expected-arrivals/{consumableId}/delete</c>。
/// - 断ったときは、何も変えていない。頼みの形が正しくない（名前が空、個数が1未満など）：400。
/// 今はログインなしで開く（docs/backlog.md #23）。
/// </remarks>
internal static partial class ConfigurationEndpoints
{
    public static void MapConfigurationEndpoints(this IEndpointRouteBuilder endpoints, ConsumablesService consumables, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(consumables);
        ArgumentNullException.ThrowIfNull(logger);

        endpoints.MapPost("/configuration/process-types", (ProcessTypeBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "加工の種類の追加", body.Id ?? "", () =>
                consumables.AddProcessTypeAsync(ToProcessType(body.Id, body), cancellationToken)));

        endpoints.MapPost("/configuration/process-types/{id}/edit", (string id, ProcessTypeBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "加工の種類の変更", id, () =>
                consumables.UpdateProcessTypeAsync(ToProcessType(id, body), cancellationToken)));

        endpoints.MapPost("/configuration/process-types/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "加工の種類の削除", id, () =>
                consumables.DeleteProcessTypeAsync(new ProcessTypeId(Required(id, "加工の種類")), cancellationToken)));

        endpoints.MapPost("/configuration/varieties", (VarietyBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "品種の追加", body.Id ?? "", () =>
                consumables.AddVarietyAsync(new VarietyId(Required(body.Id, "品種")), new ProcessTypeId(Required(body.ProcessType, "加工の種類")), cancellationToken)));

        endpoints.MapPost("/configuration/varieties/{id}/edit", (string id, VarietyBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "品種の変更", id, () =>
                consumables.UpdateVarietyAsync(new VarietyId(Required(id, "品種")), new ProcessTypeId(Required(body.ProcessType, "加工の種類")), cancellationToken)));

        endpoints.MapPost("/configuration/varieties/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "品種の削除", id, () =>
                consumables.DeleteVarietyAsync(new VarietyId(Required(id, "品種")), cancellationToken)));

        endpoints.MapPost("/configuration/varieties/{id}/expected-arrivals", (string id, ExpectedArrivalBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "入庫の予定の追加", body.Consumable ?? "", () =>
                consumables.AddExpectedArrivalAsync(new ConsumableId(Required(body.Consumable, "消耗品の ID")), new VarietyId(Required(id, "品種")), cancellationToken)));

        endpoints.MapPost("/configuration/expected-arrivals/{consumableId}/delete", (string consumableId, CancellationToken cancellationToken) =>
            RunAsync(logger, "入庫の予定の削除", consumableId, () =>
                consumables.RemoveExpectedArrivalAsync(new ConsumableId(Required(consumableId, "消耗品の ID")), cancellationToken)));
    }

    private static ProcessTypeSettings ToProcessType(string? id, ProcessTypeBody body) =>
        new(
            new ProcessTypeId(Required(id, "加工の種類")),
            new CarrierTypeId(Required(body.Carrier, "キャリアの種類")),
            body.Capacity ?? throw new ArgumentException("キャリアに入る個数を指定してください。", nameof(body)));

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name}を指定してください。", nameof(value)) : value;

    private static string ResultText(ConfigurationChangeResult result) => result switch
    {
        ConfigurationChangeResult.Saved => "saved",
        ConfigurationChangeResult.AlreadyRegistered => "alreadyRegistered",
        ConfigurationChangeResult.NotFound => "notFound",
        ConfigurationChangeResult.InUse => "inUse",
        _ => throw new InvalidOperationException($"知らない消耗品の構成の変更の結果です（{result}）。"),
    };

    private static async Task<IResult> RunAsync(ILogger logger, string operation, string target, Func<Task<ConfigurationChangeResult>> action)
    {
        try
        {
            var result = ResultText(await action().ConfigureAwait(false));
            LogDone(logger, operation, target, result);
            return Results.Json(new OperationResponse(result));
        }
        catch (ArgumentException exception)
        {
            // 頼みの形が正しくない（名前が空、個数が1未満など）。値を作れないので、消耗品の管理には届いておらず、何も変えていない
            LogRefused(logger, operation, target, exception.Message);
            return Results.BadRequest(new OperationResponse("invalid"));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）の結果：{Result}")]
    private static partial void LogDone(ILogger logger, string operation, string target, string result);

    [LoggerMessage(Level = LogLevel.Information, Message = "画面からの {Operation}（{Target}）を受け付けませんでした：{Reason}")]
    private static partial void LogRefused(ILogger logger, string operation, string target, string reason);

    private sealed record OperationResponse(string Result);

    private sealed record ProcessTypeBody(string? Id, string? Carrier, int? Capacity);

    private sealed record VarietyBody(string? Id, string? ProcessType);

    private sealed record ExpectedArrivalBody(string? Consumable);
}
