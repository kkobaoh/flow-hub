using FlowHub.Api.Composition.Screens;
using FlowHub.Notifications;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Tests;

/// <summary>
/// 画面へ送る様子（変わったら番号を進めて、丸ごと送る）のテスト。
/// </summary>
public sealed class ScreenFeedTests : IDisposable
{
    private static readonly AgvSnapshot Agv1AtP1 = new("AGV-1", "P1", 90, "idle", "auto", CarrierMoving: false, BatteryPercent: 80);
    private static readonly AgvSnapshot Agv1AtP2 = Agv1AtP1 with { Panel = "P2" };

    private readonly RecordingBroadcast _broadcast = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ScreenFeed _feed;
    private bool _running;
    private Func<IReadOnlyList<AgvSnapshot>> _agvs = () => [];
    private Func<IReadOnlyList<Notice>> _notices = () => [];

    public ScreenFeedTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        _feed = new ScreenFeed(_broadcast, TimeProvider.System, _loggerFactory.CreateLogger<ScreenFeed>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    private void Attach() => _feed.Attach(new ScreenSources(() => _running, () => _agvs(), () => _notices()));

    [Fact]
    public void つなぐ前は様子がない()
    {
        Assert.Empty(_feed.Current());
    }

    [Fact]
    public void つないだときの様子はどれも番号1()
    {
        _running = true;
        _agvs = () => [Agv1AtP1];
        Attach();

        Assert.Equal(
            [
                new ScreenMessage(ScreenFeed.SystemStatusMethod, new SystemStatusSnapshot(1, "started")),
                new ScreenMessage(ScreenFeed.AgvsMethod, new AgvsSnapshot(1, [Agv1AtP1])),
                new ScreenMessage(ScreenFeed.MessagesMethod, new MessagesSnapshot(1, [])),
            ],
            _feed.Current(),
            MessageComparer.Instance);
        Assert.Empty(_broadcast.Sent); // つないだだけでは送らない（画面は、つないだときに今の様子を受け取る）
    }

    [Fact]
    public async Task 運転状態が変わったら番号を進めて全員に送る()
    {
        _running = true;
        Attach();

        _running = false;
        await _feed.PublishIfChangedAsync(Token);

        var sent = Assert.Single(_broadcast.Sent);
        Assert.Equal(ScreenFeed.SystemStatusMethod, sent.Method);
        Assert.Equal(new SystemStatusSnapshot(2, "stopped"), sent.Payload);
    }

    [Fact]
    public async Task AGVの様子が変わったらAGVの番号だけを進めて送る()
    {
        _agvs = () => [Agv1AtP1];
        Attach();

        _agvs = () => [Agv1AtP2];
        await _feed.PublishIfChangedAsync(Token);

        var sent = Assert.Single(_broadcast.Sent);
        Assert.Equal(ScreenFeed.AgvsMethod, sent.Method);
        var snapshot = Assert.IsType<AgvsSnapshot>(sent.Payload);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal([Agv1AtP2], snapshot.Agvs);
        Assert.Contains(_feed.Current(), m => m.Payload is SystemStatusSnapshot { Version: 1 }); // 運転状態の番号は進めない
    }

    [Fact]
    public async Task 知らせが増えたらメッセージの番号を進めて新しい順に送る()
    {
        var at = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        var first = new Notice(1, at, "搬送を中断しました", "搬送 #11");
        _notices = () => [first];
        Attach();

        var second = new Notice(2, at.AddMinutes(1), "行ける充電ポートがありません", "AGV-2");
        _notices = () => [second, first];
        await _feed.PublishIfChangedAsync(Token);
        _notices = () => [second, first]; // 同じ知らせを読み直しても送らない
        await _feed.PublishIfChangedAsync(Token);

        var sent = Assert.Single(_broadcast.Sent);
        Assert.Equal(ScreenFeed.MessagesMethod, sent.Method);
        var snapshot = Assert.IsType<MessagesSnapshot>(sent.Payload);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(
            [new MessageSnapshot(2, at.AddMinutes(1), "行ける充電ポートがありません", "AGV-2"), new MessageSnapshot(1, at, "搬送を中断しました", "搬送 #11")],
            snapshot.Messages);
    }

    [Fact]
    public async Task 変わっていなければ送らない()
    {
        _running = true;
        _agvs = () => [Agv1AtP1];
        Attach();

        _agvs = () => [Agv1AtP1 with { }]; // 中身が同じ別の並び
        await _feed.PublishIfChangedAsync(Token);

        Assert.Empty(_broadcast.Sent);
    }

    [Fact]
    public async Task つなぐ前に呼んでも何もしない()
    {
        await _feed.PublishIfChangedAsync(Token);

        Assert.Empty(_broadcast.Sent);
    }

    [Fact]
    public async Task あとから読み始めたものが先に反映されたら先に読み始めた古い様子は捨てる()
    {
        _agvs = () => [Agv1AtP1];
        Attach();

        // 1つめの読み直しは、読んでいる途中で止めておく（そのあいだに AGV が P2 へ動き、2つめの読み直しが先に終わる）
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _agvs = () =>
        {
            reading.Set();
            release.Wait(Token);
            return [Agv1AtP1 with { Panel = "P1" }];
        };
        var first = Task.Run(() => _feed.PublishIfChangedAsync(Token), Token);
        reading.Wait(Token);

        _agvs = () => [Agv1AtP2];
        await _feed.PublishIfChangedAsync(Token);
        release.Set();
        await first;

        var sent = Assert.Single(_broadcast.Sent);
        Assert.Equal([Agv1AtP2], Assert.IsType<AgvsSnapshot>(sent.Payload).Agvs);
        Assert.Contains(_feed.Current(), m => m.Payload is AgvsSnapshot { Version: 2 } s && s.Agvs.SequenceEqual([Agv1AtP2]));
    }

    [Fact]
    public async Task 送れなかったら警告をログに残して続ける()
    {
        _running = true;
        Attach();
        _broadcast.Failure = new IOException("切れた");

        _running = false;
        await _feed.PublishIfChangedAsync(Token);

        // 様子は進めたまま（画面は、次の変化か、つなぎ直したときに最新を受け取る）
        Assert.Contains(_feed.Current(), m => m.Payload is SystemStatusSnapshot { Version: 2 });
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("画面", StringComparison.Ordinal));
    }

    private sealed class RecordingBroadcast : IScreenBroadcast
    {
        private readonly List<(string Method, object Payload)> _sent = [];

        public IReadOnlyList<(string Method, object Payload)> Sent
        {
            get
            {
                lock (_sent)
                {
                    return [.. _sent];
                }
            }
        }

        public Exception? Failure { get; set; }

        public Task SendAsync(string method, object payload, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            lock (_sent)
            {
                _sent.Add((method, payload));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>送るものを、AGV の並びは中身で比べる。</summary>
    private sealed class MessageComparer : IEqualityComparer<ScreenMessage>
    {
        public static readonly MessageComparer Instance = new();

        public bool Equals(ScreenMessage? x, ScreenMessage? y) => (x, y) switch
        {
            ({ Payload: AgvsSnapshot a }, { Payload: AgvsSnapshot b }) => x.Method == y.Method && a.Version == b.Version && a.Agvs.SequenceEqual(b.Agvs),
            ({ Payload: MessagesSnapshot a }, { Payload: MessagesSnapshot b }) => x.Method == y.Method && a.Version == b.Version && a.Messages.SequenceEqual(b.Messages),
            _ => object.Equals(x, y),
        };

        public int GetHashCode(ScreenMessage obj) => obj.Method.GetHashCode(StringComparison.Ordinal);
    }
}
