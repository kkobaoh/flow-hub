using FlowHub.Consumables.Domain;

namespace FlowHub.Consumables;

/// <summary>
/// 消耗品の管理が配車に渡す「受け渡しの相手」の参照を、画面に出す名前にする。参照の書き方は、消耗品の管理の中に閉じ込めておく。
/// </summary>
public static class TransferEndpointNames
{
    /// <summary>
    /// 装置やステーションの参照なら、その番号（DEV-1、ST-1 など）。読めない参照（消耗品の管理が渡したものでない）は、そのまま返す。
    /// </summary>
    public static string DisplayName(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            var (device, station) = Endpoints.Parse(endpoint);
            return device?.Value ?? station!.Value.Value;
        }
        catch (ArgumentException)
        {
            // 消耗品の管理が渡した参照でない（ワークの搬送など）。画面には、届いた参照のまま出す
            return endpoint;
        }
    }
}
