namespace FlowHub.Simulator;

/// <summary>
/// 装置・ステーション・上位システムのシミュレーターの設定（appsettings の「Simulator」）。待ち時間の既定値は仮の値。
/// </summary>
public sealed record SimulatorOptions
{
    /// <summary>装置に受け渡しの許可を出してから、完了を通知するまでの待ち時間（既定は10秒）。</summary>
    public TimeSpan DeviceTransferDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>ステーションに受け渡しの許可を出してから、完了を通知するまでの待ち時間（既定は10秒）。</summary>
    public TimeSpan StationTransferDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>ステーションに出庫を頼んでから、「出た」を通知するまでの待ち時間（既定は10秒）。</summary>
    public TimeSpan ShipmentDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>上位システムに補充を頼んでから、入庫するまでの待ち時間（既定は60秒）。</summary>
    public TimeSpan ReplenishmentDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>補充を入庫するステーションの ID。指定がなければ、ID の順で一番目のステーション。</summary>
    public string? ReplenishmentStation { get; init; }
}
