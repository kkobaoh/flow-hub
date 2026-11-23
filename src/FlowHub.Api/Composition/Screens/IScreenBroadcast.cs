namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// つないでいるすべての画面へ送る。本物は SignalR のハブ（<see cref="ScreenHub"/>）を通す。
/// </summary>
internal interface IScreenBroadcast
{
    Task SendAsync(string method, object payload, CancellationToken cancellationToken);
}
