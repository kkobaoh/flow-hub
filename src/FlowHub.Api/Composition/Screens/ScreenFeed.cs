using FlowHub.Notifications;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送る様子の読み元。
/// </summary>
/// <param name="IsRunning">運転状態がスタートか。</param>
/// <param name="Agvs">すべての AGV の今の様子（ID の順）。</param>
/// <param name="Notices">残しているメッセージ（新しい順）。</param>
internal sealed record ScreenSources(Func<bool> IsRunning, Func<IReadOnlyList<AgvSnapshot>> Agvs, Func<IReadOnlyList<Notice>> Notices);

/// <summary>画面へ送るもの（受け取る名前と、中身）。</summary>
internal sealed record ScreenMessage(string Method, object Payload);

/// <summary>
/// 画面へ送る様子を持つ。様子の種類（運転状態、AGV、メッセージ）ごとに番号を付け、変わったら番号を進めて、つないでいるすべての画面へ丸ごと送る。
/// </summary>
/// <remarks>
/// 様子は、処理のあと（<see cref="FlowHubSystem"/> の後始末）に読み直して、変わったときだけ送る。
/// 読み元（交通整理など）は、この lock の外で読む（lock の中から、ほかの集約の lock を取らないため）。
/// 読み始める前に番号札を取り、自分より後に読み始めたものがもう反映されていれば、自分の読んだ様子は捨てる
/// （後に読み始めたものは、それより前に終わった変化をすべて含んでいる。変化のあとには必ず読み直すので、最新は失われない）。
/// 送るのは lock の外で行う（送る順が入れ替わっても、画面は古い番号を捨てる）。
/// 送れなかったときは、警告をログに残して続ける。画面は、次に変わったときか、つなぎ直したときに最新の様子を受け取る。
/// </remarks>
internal sealed partial class ScreenFeed(IScreenBroadcast broadcast, TimeProvider timeProvider, ILogger<ScreenFeed> logger)
{
    /// <summary>運転状態の様子を送るときの名前（画面の側も、この名前で受け取る）。</summary>
    public const string SystemStatusMethod = "systemStatus";

    /// <summary>AGV の様子を送るときの名前（画面の側も、この名前で受け取る）。</summary>
    public const string AgvsMethod = "agvs";

    /// <summary>メッセージの様子を送るときの名前（画面の側も、この名前で受け取る）。</summary>
    public const string MessagesMethod = "messages";

    /// <summary>送るのを待つ長さ。</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private ScreenSources? _sources;
    private Topic<string>? _systemStatus;
    private Topic<IReadOnlyList<AgvSnapshot>>? _agvs;
    private Topic<IReadOnlyList<Notice>>? _messages;
    private long _issuedTickets;
    private long _appliedTicket;

    /// <summary>
    /// 様子の読み元をつなぐ。つないだときの様子を、それぞれ番号1にする（まだ送らない。画面は、つないだときに受け取る）。
    /// </summary>
    public void Attach(ScreenSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // 読み元は lock の外で読む
        var running = RunText(sources.IsRunning());
        var agvs = sources.Agvs();
        var notices = sources.Notices();
        lock (_lock)
        {
            if (_sources is not null)
            {
                throw new InvalidOperationException("様子の読み元は、もうつないであります。");
            }

            _sources = sources;
            _systemStatus = new Topic<string>(SystemStatusMethod, running, (version, state) => new SystemStatusSnapshot(version, state), StringComparer.Ordinal);
            _agvs = new Topic<IReadOnlyList<AgvSnapshot>>(AgvsMethod, agvs, (version, list) => new AgvsSnapshot(version, list), AgvListComparer.Instance);
            _messages = new Topic<IReadOnlyList<Notice>>(MessagesMethod, notices, ToMessages, NoticeListComparer.Instance);
        }
    }

    /// <summary>今の様子（種類ごとに1つ）。つなぐ前は空。</summary>
    public IReadOnlyList<ScreenMessage> Current()
    {
        lock (_lock)
        {
            return _systemStatus is null || _agvs is null || _messages is null ? [] : [_systemStatus.Current, _agvs.Current, _messages.Current];
        }
    }

    /// <summary>
    /// 様子を読み直し、変わっていたものは番号を進めて、すべての画面へ送る。
    /// </summary>
    public async Task PublishIfChangedAsync(CancellationToken cancellationToken)
    {
        ScreenSources sources;
        long ticket;
        lock (_lock)
        {
            if (_sources is null)
            {
                return; // まだつないでいない
            }

            sources = _sources;
            ticket = ++_issuedTickets;
        }

        // 読み元は lock の外で読む
        var running = RunText(sources.IsRunning());
        var agvs = sources.Agvs();
        var notices = sources.Notices();

        List<ScreenMessage> changed = [];
        lock (_lock)
        {
            if (ticket < _appliedTicket)
            {
                return; // あとに読み始めたものが、もう反映されている（そちらの方が新しい）
            }

            _appliedTicket = ticket;
            if (_systemStatus!.TryAdvance(running, out var status))
            {
                changed.Add(status);
            }

            if (_agvs!.TryAdvance(agvs, out var agvMessage))
            {
                changed.Add(agvMessage);
            }

            if (_messages!.TryAdvance(notices, out var messages))
            {
                changed.Add(messages);
            }
        }

        foreach (var message in changed)
        {
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(ScreenMessage message, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(SendTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await broadcast.SendAsync(message.Method, message.Payload, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // 様子は進めたまま。画面は、次に変わったときか、つなぎ直したときに最新を受け取る
            LogSendFailed(logger, exception, message.Method);
        }
    }

    private static string RunText(bool running) => running ? "started" : "stopped";

    private static MessagesSnapshot ToMessages(long version, IReadOnlyList<Notice> notices) =>
        new(version, [.. notices.Select(MessageSnapshot.From)]);

    [LoggerMessage(Level = LogLevel.Warning, Message = "画面へ様子（{Method}）を送れませんでした。画面は、次に変わったときか、つなぎ直したときに最新を受け取ります。")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string method);

    /// <summary>
    /// 様子の1つの種類。今の様子と番号を持ち、変わったら番号を進める。呼ぶ側の lock の中でだけ使う。
    /// </summary>
    private sealed class Topic<TState>(string method, TState initial, Func<long, TState, object> toPayload, IEqualityComparer<TState> comparer)
    {
        private TState _state = initial;
        private long _version = 1;

        public ScreenMessage Current { get; private set; } = new(method, toPayload(1, initial));

        public bool TryAdvance(TState state, out ScreenMessage message)
        {
            if (comparer.Equals(state, _state))
            {
                message = Current;
                return false;
            }

            _state = state;
            _version++;
            Current = new ScreenMessage(method, toPayload(_version, state));
            message = Current;
            return true;
        }
    }

    /// <summary>AGV の様子の並びを、中身で比べる。</summary>
    private sealed class AgvListComparer : IEqualityComparer<IReadOnlyList<AgvSnapshot>>
    {
        public static readonly AgvListComparer Instance = new();

        public bool Equals(IReadOnlyList<AgvSnapshot>? x, IReadOnlyList<AgvSnapshot>? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

        public int GetHashCode(IReadOnlyList<AgvSnapshot> obj) => obj.Count;
    }

    /// <summary>メッセージの並びを比べる（知らせは変わらないので、番号の並びが同じなら同じ）。</summary>
    private sealed class NoticeListComparer : IEqualityComparer<IReadOnlyList<Notice>>
    {
        public static readonly NoticeListComparer Instance = new();

        public bool Equals(IReadOnlyList<Notice>? x, IReadOnlyList<Notice>? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.Select(n => n.Sequence).SequenceEqual(y.Select(n => n.Sequence)));

        public int GetHashCode(IReadOnlyList<Notice> obj) => obj.Count;
    }
}
