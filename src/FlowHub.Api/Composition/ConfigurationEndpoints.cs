using FlowHub.Consumables;
using FlowHub.Consumables.Application;
using FlowHub.Dispatching;
using FlowHub.Map;
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
/// - 装置：<c>POST /configuration/devices</c>（<c>{ "id", "panel", "slots": [ストッカーごとのスロットの数] }</c>）、<c>/configuration/devices/{id}/edit</c>（<c>{ "panel", "slots" }</c>）、<c>/configuration/devices/{id}/delete</c>。
/// - ステーション：<c>POST /configuration/stations</c>（<c>{ "id", "panel" }</c>）、<c>/configuration/stations/{id}/edit</c>（<c>{ "panel" }</c>）、<c>/configuration/stations/{id}/delete</c>。
///   装置とステーションでは、ほかに <c>panelUnavailable</c>、<c>slotsOccupied</c>、<c>exceedsStockerSetting</c>、<c>stationLimit</c>。
/// - 入庫の予定：<c>POST /configuration/varieties/{id}/expected-arrivals</c>（<c>{ "consumable" }</c>）、<c>/configuration/expected-arrivals/{consumableId}/delete</c>。
/// - 断ったときは、何も変えていない。頼みの形が正しくない（名前が空、個数が1未満など）：400。
/// 構成の変更権限と CSRF トークンが必要。
/// </remarks>
internal static partial class ConfigurationEndpoints
{
    /// <param name="transferPanels">公開中のマップの受け渡しパネル（特殊消耗品受け渡しパネルを含む）を読む。</param>
    public static void MapConfigurationEndpoints(this IEndpointRouteBuilder endpoints, ConsumablesService consumables, Func<IReadOnlySet<PanelId>> transferPanels, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(consumables);
        ArgumentNullException.ThrowIfNull(transferPanels);
        ArgumentNullException.ThrowIfNull(logger);

        var commands = endpoints.MapGroup("/configuration").RequireScreenCommand("configure");
        MapEquipmentEndpoints(commands, consumables, transferPanels, logger);

        commands.MapPost("/process-types", (ProcessTypeBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "加工の種類の追加", body.Id ?? "", () =>
                consumables.AddProcessTypeAsync(ToProcessType(body.Id, body), cancellationToken)));

        commands.MapPost("/process-types/{id}/edit", (string id, ProcessTypeBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "加工の種類の変更", id, () =>
                consumables.UpdateProcessTypeAsync(ToProcessType(id, body), cancellationToken)));

        commands.MapPost("/process-types/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "加工の種類の削除", id, () =>
                consumables.DeleteProcessTypeAsync(new ProcessTypeId(Required(id, "加工の種類")), cancellationToken)));

        commands.MapPost("/varieties", (VarietyBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "品種の追加", body.Id ?? "", () =>
                consumables.AddVarietyAsync(new VarietyId(Required(body.Id, "品種")), new ProcessTypeId(Required(body.ProcessType, "加工の種類")), cancellationToken)));

        commands.MapPost("/varieties/{id}/edit", (string id, VarietyBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "品種の変更", id, () =>
                consumables.UpdateVarietyAsync(new VarietyId(Required(id, "品種")), new ProcessTypeId(Required(body.ProcessType, "加工の種類")), cancellationToken)));

        commands.MapPost("/varieties/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "品種の削除", id, () =>
                consumables.DeleteVarietyAsync(new VarietyId(Required(id, "品種")), cancellationToken)));

        commands.MapPost("/varieties/{id}/expected-arrivals", (string id, ExpectedArrivalBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "入庫の予定の追加", body.Consumable ?? "", () =>
                consumables.AddExpectedArrivalAsync(new ConsumableId(Required(body.Consumable, "消耗品の ID")), new VarietyId(Required(id, "品種")), cancellationToken)));

        commands.MapPost("/expected-arrivals/{consumableId}/delete", (string consumableId, CancellationToken cancellationToken) =>
            RunAsync(logger, "入庫の予定の削除", consumableId, () =>
                consumables.RemoveExpectedArrivalAsync(new ConsumableId(Required(consumableId, "消耗品の ID")), cancellationToken)));
    }

    private static void MapEquipmentEndpoints(IEndpointRouteBuilder commands, ConsumablesService consumables, Func<IReadOnlySet<PanelId>> transferPanels, ILogger logger)
    {
        commands.MapPost("/devices", (DeviceBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "装置の追加", body.Id ?? "", () =>
                consumables.AddDeviceAsync(ToDevice(body.Id, body), transferPanels(), cancellationToken)));

        commands.MapPost("/devices/{id}/edit", (string id, DeviceBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "装置の変更", id, () =>
                consumables.UpdateDeviceAsync(ToDevice(id, body), transferPanels(), cancellationToken)));

        commands.MapPost("/devices/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "装置の削除", id, () =>
                consumables.DeleteDeviceAsync(new DeviceId(Required(id, "装置")), cancellationToken)));

        commands.MapPost("/stations", (StationBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "ステーションの追加", body.Id ?? "", () =>
                consumables.AddStationAsync(ToStation(body.Id, body), transferPanels(), cancellationToken)));

        commands.MapPost("/stations/{id}/edit", (string id, StationBody body, CancellationToken cancellationToken) =>
            RunAsync(logger, "ステーションの変更", id, () =>
                consumables.UpdateStationAsync(ToStation(id, body), transferPanels(), cancellationToken)));

        commands.MapPost("/stations/{id}/delete", (string id, CancellationToken cancellationToken) =>
            RunAsync(logger, "ステーションの削除", id, () =>
                consumables.DeleteStationAsync(new StationId(Required(id, "ステーション")), cancellationToken)));
    }

    private static DeviceLayout ToDevice(string? id, DeviceBody body) =>
        new(
            new DeviceId(Required(id, "装置")),
            new PanelId(Required(body.Panel, "受け渡しパネル")),
            body.Slots ?? throw new ArgumentException("ストッカーごとのスロットの数を指定してください。", nameof(body)));

    private static StationLayout ToStation(string? id, StationBody body) =>
        new(new StationId(Required(id, "ステーション")), new PanelId(Required(body.Panel, "受け渡しパネル")));

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
        ConfigurationChangeResult.PanelUnavailable => "panelUnavailable",
        ConfigurationChangeResult.SlotsOccupied => "slotsOccupied",
        ConfigurationChangeResult.ExceedsStockerSetting => "exceedsStockerSetting",
        ConfigurationChangeResult.StationLimit => "stationLimit",
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

    private sealed record DeviceBody(string? Id, string? Panel, IReadOnlyList<int>? Slots);

    private sealed record StationBody(string? Id, string? Panel);
}
