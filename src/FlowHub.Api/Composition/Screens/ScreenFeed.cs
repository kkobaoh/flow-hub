using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る様子を持つ。変わったら番号を進めて、つないでいるすべての画面へ丸ごと送る。
/// </summary>
/// <remarks>
/// 様子は、処理のあと（<see cref="FlowHubSystem"/> の後始末）に読み直して、変わったときだけ送る。
/// 番号と様子は lock の中で決め、送るのは lock の外で行う（送る順が入れ替わっても、画面は古い番号を捨てる）。
/// 送れなかったときは、警告をログに残して続ける。画面は、次に変わったときか、つなぎ直したときに最新の様子を受け取る。
/// </remarks>
internal sealed partial class ScreenFeed(IScreenBroadcast broadcast, TimeProvider timeProvider, ILogger<ScreenFeed> logger)
{
    /// <summary>運転状態の様子を送るときの名前（画面の側も、この名前で受け取る）。</summary>
    public const string SystemStatusMethod = "systemStatus";

    /// <summary>送るのを待つ長さ。</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private Func<bool>? _isRunning;
    private SystemStatusSnapshot? _current;

    /// <summary>
    /// 様子を読む元をつなぐ。つないだときの様子を番号1にする（まだ送らない。画面は、つないだときに受け取る）。
    /// </summary>
    /// <param name="isRunning">運転状態がスタートか。</param>
    public void Attach(Func<bool> isRunning)
    {
        ArgumentNullException.ThrowIfNull(isRunning);
        lock (_lock)
        {
            if (_isRunning is not null)
            {
                throw new InvalidOperationException("様子を読む元は、もうつないであります。");
            }

            _isRunning = isRunning;
            _current = new SystemStatusSnapshot(1, ToText(isRunning()));
        }
    }

    /// <summary>今の様子。つなぐ前は、ない。</summary>
    public bool TryGetCurrent([NotNullWhen(true)] out SystemStatusSnapshot? current)
    {
        lock (_lock)
        {
            current = _current;
            return current is not null;
        }
    }

    /// <summary>
    /// 様子を読み直し、変わっていたら番号を進めて、すべての画面へ送る。
    /// </summary>
    public async Task PublishIfChangedAsync(CancellationToken cancellationToken)
    {
        SystemStatusSnapshot changed;
        lock (_lock)
        {
            if (_isRunning is null || _current is null)
            {
                return; // まだつないでいない
            }

            var state = ToText(_isRunning());
            if (state == _current.State)
            {
                return;
            }

            changed = new SystemStatusSnapshot(_current.Version + 1, state);
            _current = changed;
        }

        using var timeout = new CancellationTokenSource(SendTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await broadcast.SendAsync(SystemStatusMethod, changed, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 様子は進めたまま。画面は、次に変わったときか、つなぎ直したときに最新を受け取る
            LogSendFailed(logger, exception, changed.Version);
        }
    }

    private static string ToText(bool running) => running ? "started" : "stopped";

    [LoggerMessage(Level = LogLevel.Warning, Message = "画面へ運転状態の様子（番号 {Version}）を送れませんでした。画面は、次に変わったときか、つなぎ直したときに最新を受け取ります。")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, long version);
}
