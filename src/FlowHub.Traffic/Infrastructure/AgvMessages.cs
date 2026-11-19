using System.Text.Json;
using System.Text.Json.Nodes;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Domain;

namespace FlowHub.Traffic.Infrastructure;

/// <summary>AGV からの通知（docs/agv-protocol.md の「通知」）。</summary>
internal abstract record AgvNotification;

internal sealed record MotionReceivedNotification(long Number) : AgvNotification;

internal sealed record MovedNotification(long Number, MoveDirection Direction, PanelId Panel) : AgvNotification;

internal sealed record RotatedNotification(long Number, int Heading) : AgvNotification;

internal sealed record CarrierLoweredNotification(long Number) : AgvNotification;

internal sealed record CarrierRaisedNotification(long Number) : AgvNotification;

internal sealed record CarrierHeightMeasuredNotification(long Number, int HeightMm) : AgvNotification;

internal sealed record AlarmClearedNotification(long Number) : AgvNotification;

internal sealed record PositionResetCompletedNotification(long Number) : AgvNotification;

internal sealed record ErrorNotification(AgvErrorKind Kind) : AgvNotification;

internal sealed record BatteryNotification(int Percent) : AgvNotification;

/// <summary>
/// AGV とやり取りする JSON の形式（docs/agv-protocol.md）への変換と読み取り。
/// </summary>
internal static class AgvMessages
{
    private static readonly Dictionary<string, AgvErrorKind> ErrorKinds = new(StringComparer.Ordinal)
    {
        ["crossMissed"] = AgvErrorKind.CrossMissed,
        ["crossFalselyDetected"] = AgvErrorKind.CrossFalselyDetected,
        ["bumperPressed"] = AgvErrorKind.BumperPressed,
        ["communicationLost"] = AgvErrorKind.CommunicationLost,
        ["batteryDepleted"] = AgvErrorKind.BatteryDepleted,
        ["motorDriverError"] = AgvErrorKind.MotorDriverError,
        ["hardwareError"] = AgvErrorKind.HardwareError,
        ["serverRequestedStop"] = AgvErrorKind.ServerRequestedStop,
    };

    /// <summary>コマンドを取りに来た GET への応答。</summary>
    public static JsonObject Commands(IEnumerable<AgvCommand> commands) =>
        new() { ["commands"] = new JsonArray([.. commands.Select(Command)]) };

    /// <summary>位置を取りに来た GET への応答。</summary>
    public static JsonObject Position(Pose pose) =>
        new() { ["panel"] = pose.NodeId, ["heading"] = pose.Heading };

    private static JsonObject Command(AgvCommand command)
    {
        var json = new JsonObject { ["number"] = command.Number };
        switch (command)
        {
            case MotionCommand motion:
                json["type"] = "motion";
                json["steps"] = new JsonArray([.. motion.Steps.Select(Step)]);
                break;
            case EmergencyStopCommand:
                json["type"] = "emergencyStop";
                break;
            case AlarmClearCommand:
                json["type"] = "alarmClear";
                break;
            case PositionResetCommand reset:
                json["type"] = "positionReset";
                json["panel"] = reset.Position.NodeId;
                json["heading"] = reset.Position.Heading;
                break;
            case LowerCarrierCommand lower:
                json["type"] = "lowerCarrier";
                if (lower.HeightMm is { } height)
                {
                    json["heightMm"] = height;
                }

                break;
            case RaiseCarrierCommand:
                json["type"] = "raiseCarrier";
                break;
            default:
                throw new InvalidOperationException($"未知のコマンドです：{command.GetType().Name}");
        }

        return json;
    }

    private static JsonObject Step(TravelCommand step) => step switch
    {
        ForwardCommand forward => new JsonObject { ["action"] = "forward", ["distanceMm"] = forward.DistanceMm, ["panel"] = forward.To.Value },
        BackwardCommand backward => new JsonObject { ["action"] = "backward", ["distanceMm"] = backward.DistanceMm, ["panel"] = backward.To.Value },
        RotateCommand rotate => new JsonObject { ["action"] = "rotate", ["heading"] = rotate.Heading },
        FreeRotateCommand free => new JsonObject { ["action"] = "rotate_free", ["heading"] = free.Heading },
        _ => throw new InvalidOperationException($"未知の動作です：{step.GetType().Name}"),
    };

    /// <summary>
    /// 通知の JSON を読み取る。形式が読めない（知らない種類、項目が足りない、壊れた内容）ときは null。
    /// </summary>
    public static AgvNotification? ParseNotification(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ParseNotification(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgvNotification? ParseNotification(JsonElement json)
    {
        var type = String(json, "type");
        return type switch
        {
            "error" => String(json, "kind") is { } kind && ErrorKinds.TryGetValue(kind, out var errorKind) ? new ErrorNotification(errorKind) : null,
            "battery" => Int(json, "percent") is { } percent && percent is >= 0 and <= 100 ? new BatteryNotification(percent) : null,
            _ => Long(json, "number") is { } number ? ParseCompletion(json, type, number) : null,
        };
    }

    private static AgvNotification? ParseCompletion(JsonElement json, string? type, long number) => type switch
    {
        "motionReceived" => new MotionReceivedNotification(number),
        "moved" => Direction(String(json, "direction")) is { } direction && String(json, "panel") is { Length: > 0 } panel
            ? new MovedNotification(number, direction, new PanelId(panel))
            : null,
        "rotated" => Int(json, "heading") is { } heading && heading is >= 0 and < 360 ? new RotatedNotification(number, heading) : null,
        "carrierLowered" => new CarrierLoweredNotification(number),
        "carrierRaised" => new CarrierRaisedNotification(number),
        "carrierHeightMeasured" => Int(json, "heightMm") is { } height ? new CarrierHeightMeasuredNotification(number, height) : null,
        "alarmCleared" => new AlarmClearedNotification(number),
        "positionResetCompleted" => new PositionResetCompletedNotification(number),
        _ => null,
    };

    private static MoveDirection? Direction(string? direction) => direction switch
    {
        "forward" => MoveDirection.Forward,
        "backward" => MoveDirection.Backward,
        _ => null,
    };

    private static string? String(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Long(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static int? Int(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
}
