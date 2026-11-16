using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// システムから AGV へ渡すコマンド。AGV ごとに 1 から 1つずつ増やした番号を持つ。
/// AGV が GET で取りに来たときに渡す（ロングポーリング）。
/// </summary>
internal abstract record AgvCommand(long Number);

/// <summary>
/// 動作コマンド。1つの区間ぶんの、前進・後退・回転の組み合わせの配列を持つ。
/// </summary>
internal sealed record MotionCommand(long Number, IReadOnlyList<TravelCommand> Steps) : AgvCommand(Number)
{
    public bool Equals(MotionCommand? other) =>
        other is not null && Number == other.Number && Steps.SequenceEqual(other.Steps);

    public override int GetHashCode() => Number.GetHashCode();
}

/// <summary>
/// 緊急停止。完了の通知はなく、AGV はアラームを発報してエラー「サーバー要求エラー停止」を通知する。
/// </summary>
internal sealed record EmergencyStopCommand(long Number) : AgvCommand(Number);

/// <summary>
/// アラームクリア。AGV は内部に持っている残りの指示の続きを走り、アラームクリアの完了を通知する。
/// </summary>
internal sealed record AlarmClearCommand(long Number) : AgvCommand(Number);

/// <summary>
/// 位置リセット。AGV の位置（パネルと絶対角度）を上書きする。AGV はアラームをクリアし、内部の指示を破棄して、完了を通知する。
/// </summary>
internal sealed record PositionResetCommand(long Number, Pose Position) : AgvCommand(Number);

/// <summary>
/// AGV が通知してくるエラーの種類。エラーを出した AGV は止まっている。
/// </summary>
internal enum AgvErrorKind
{
    /// <summary>十字の検出漏れ。位置がずれた可能性がある。</summary>
    CrossMissed,

    /// <summary>十字の誤検出。位置がずれた可能性がある。</summary>
    CrossFalselyDetected,

    /// <summary>バンパースイッチが押された。</summary>
    BumperPressed,

    /// <summary>通信が切れた（通信が戻ったときに通知される）。</summary>
    CommunicationLost,

    /// <summary>充電切れ。</summary>
    BatteryDepleted,

    /// <summary>モーターのドライバーのエラー。</summary>
    MotorDriverError,

    /// <summary>ハードウェアのエラー。</summary>
    HardwareError,

    /// <summary>サーバー要求エラー停止（システムが緊急停止を送ったとき）。</summary>
    ServerRequestedStop,
}

internal static class AgvErrorKindExtensions
{
    /// <summary>
    /// 位置がずれた可能性のあるエラーか。ずれた可能性があれば「異常」、なければ「緊急停止中」として扱う。
    /// </summary>
    public static bool MayShiftPosition(this AgvErrorKind kind) =>
        kind is AgvErrorKind.CrossMissed or AgvErrorKind.CrossFalselyDetected;
}

/// <summary>
/// AGV からの通知を受けた結果。
/// </summary>
internal enum NotificationOutcome
{
    /// <summary>受け付けた。</summary>
    Accepted,

    /// <summary>ログに残すだけ（状態は変えない）。</summary>
    Logged,

    /// <summary>重複して届いた通知として無視した。ログに残す。</summary>
    IgnoredDuplicate,

    /// <summary>古い通知（位置リセットで捨てた指示への通知）として無視した。ログに残す。</summary>
    IgnoredStale,

    /// <summary>予期しない通知として無視した。ログに残し、画面で警告する。</summary>
    IgnoredUnexpected,

    /// <summary>意味のわからない通知（送っていない番号など）として受け付けなかった。ログに残して知らせる。</summary>
    Rejected,

    /// <summary>実際の状態と食い違う通知。緊急停止を用意して「異常」にした。画面で知らせる。</summary>
    Fault,
}
