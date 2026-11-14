namespace FlowHub.Map;

/// <summary>
/// マップの検証の違反の種類。
/// </summary>
public enum MapValidationErrorCode
{
    /// <summary>パネルの ID が重複している。</summary>
    DuplicatePanelId,

    /// <summary>通常パネルに充電ピンが設置されている。</summary>
    ChargingPinOnNormalPanel,

    /// <summary>つながりが、存在しないパネルを指している。</summary>
    UnknownPanel,

    /// <summary>パネルが自分自身につながっている。</summary>
    SelfConnection,

    /// <summary>同じパネルの組が二重につながっている。</summary>
    DuplicateConnection,

    /// <summary>つながりの距離が正の値でない。</summary>
    NonPositiveDistance,

    /// <summary>受け渡しパネルか待機パネルが一方通行のつながりを持っている。</summary>
    OneWayToDeadEnd,

    /// <summary>受け渡しパネルか待機パネルのつながりが1つでない。</summary>
    DeadEndMustHaveSingleConnection,

    /// <summary>受け渡しパネルか待機パネルの隣が通常パネルでない。</summary>
    DeadEndMustConnectToNormalPanel,

    /// <summary>マップが離れ島に分かれている（<see cref="MapValidationOptions.RequireConnected"/> を指定したときだけ）。</summary>
    NotConnected,
}

/// <summary>
/// マップの検証の違反。
/// </summary>
/// <param name="Code">違反の種類。</param>
/// <param name="Message">画面などに表示するためのメッセージ。</param>
public sealed record MapValidationError(MapValidationErrorCode Code, string Message);
