namespace FlowHub.Consumables.Domain;

/// <summary>
/// 配車に渡す「受け渡しの相手」の参照の書き方。配車にとっては中身のわからない文字列。
/// </summary>
internal static class Endpoints
{
    private const string DevicePrefix = "device:";
    private const string StationPrefix = "station:";

    public static string Device(DeviceId id) => DevicePrefix + id.Value;

    public static string Station(StationId id) => StationPrefix + id.Value;

    /// <summary>
    /// 参照を読み取る。装置なら Device、ステーションなら Station に値が入る。
    /// </summary>
    /// <exception cref="ArgumentException">どちらの書き方でもないとき。</exception>
    public static (DeviceId? Device, StationId? Station) Parse(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpoint.StartsWith(DevicePrefix, StringComparison.Ordinal))
        {
            return (new DeviceId(endpoint[DevicePrefix.Length..]), null);
        }

        if (endpoint.StartsWith(StationPrefix, StringComparison.Ordinal))
        {
            return (null, new StationId(endpoint[StationPrefix.Length..]));
        }

        throw new ArgumentException($"受け渡しの相手の参照として読めません：{endpoint}", nameof(endpoint));
    }
}
