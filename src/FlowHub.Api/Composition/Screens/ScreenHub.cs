using Microsoft.AspNetCore.SignalR;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面とつなぐ SignalR のハブ（<c>/hubs/screen</c>）。サーバーから画面へ、様子を送るだけに使う。
/// 画面からの操作は、HTTP の窓口を通す。
/// </summary>
/// <remarks>
/// 画面がつないだとき（つなぎ直したときも）に、今の様子を丸ごと送る。
/// 今はログインなしで開く（認証は、ログインを作るときに付ける。docs/backlog.md）。
/// </remarks>
internal sealed class ScreenHub(ScreenFeed feed) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (feed.TryGetCurrent(out var current))
        {
            await Clients.Caller.SendAsync(ScreenFeed.SystemStatusMethod, current, Context.ConnectionAborted).ConfigureAwait(false);
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// SignalR のハブを通して、つないでいるすべての画面へ送る。
/// </summary>
internal sealed class HubScreenBroadcast(IHubContext<ScreenHub> hub) : IScreenBroadcast
{
    public Task SendAsync(string method, object payload, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(method, payload, cancellationToken);
}
