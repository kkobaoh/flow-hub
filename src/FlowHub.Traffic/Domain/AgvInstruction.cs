using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// システムから AGV へ送るもの。AGV との通信の形式（未定）に変換して送る。
/// </summary>
internal abstract record AgvInstruction;

/// <summary>
/// 走行の指示（1つの区間ぶん）。
/// </summary>
internal sealed record SendTravelCommands(IReadOnlyList<TravelCommand> Commands) : AgvInstruction
{
    public bool Equals(SendTravelCommands? other) =>
        other is not null && Commands.SequenceEqual(other.Commands);

    public override int GetHashCode() => Commands.Count;
}

/// <summary>
/// 緊急停止のコマンド。
/// </summary>
internal sealed record SendEmergencyStop : AgvInstruction;

/// <summary>
/// アラームクリアのコマンド。AGV は止まった地点から、残りの指示の続きを走る。
/// </summary>
internal sealed record SendAlarmClear : AgvInstruction;

/// <summary>
/// 位置リセットのコマンド。AGV の位置（パネルと絶対角度）を上書きする。AGV はアラームをクリアし、内部の指示を破棄する。
/// </summary>
internal sealed record SendPositionReset(Pose Position) : AgvInstruction;
