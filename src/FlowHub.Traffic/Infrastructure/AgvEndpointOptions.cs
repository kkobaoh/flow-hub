namespace FlowHub.Traffic.Infrastructure;

/// <summary>
/// AGV との通信の窓口の設定。
/// </summary>
public sealed record AgvEndpointOptions
{
    /// <summary>コマンドを取りに来た GET に、渡すコマンドがないときに待つ時間（既定は25秒）。</summary>
    public TimeSpan CommandWait { get; init; } = TimeSpan.FromSeconds(25);
}
