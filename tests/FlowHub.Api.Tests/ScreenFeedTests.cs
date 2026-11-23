using FlowHub.Api.Composition.Screens;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Tests;

/// <summary>
/// 画面へ送る様子（変わったら番号を進めて、丸ごと送る）のテスト。
/// </summary>
public sealed class ScreenFeedTests : IDisposable
{
    private readonly RecordingBroadcast _broadcast = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ScreenFeed _feed;
    private bool _running;

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

    [Fact]
    public void つなぐ前は様子がない()
    {
        Assert.False(_feed.TryGetCurrent(out _));
    }

    [Fact]
    public void つないだときの様子は番号1()
    {
        _running = true;
        _feed.Attach(() => _running);

        Assert.True(_feed.TryGetCurrent(out var current));
        Assert.Equal(new SystemStatusSnapshot(1, "started"), current);
        Assert.Empty(_broadcast.Sent); // つないだだけでは送らない（画面は、つないだときに今の様子を受け取る）
    }

    [Fact]
    public async Task 変わったら番号を進めて全員に送る()
    {
        _running = true;
        _feed.Attach(() => _running);

        _running = false;
        await _feed.PublishIfChangedAsync(Token);

        var sent = Assert.Single(_broadcast.Sent);
        Assert.Equal(ScreenFeed.SystemStatusMethod, sent.Method);
        Assert.Equal(new SystemStatusSnapshot(2, "stopped"), sent.Payload);
        Assert.True(_feed.TryGetCurrent(out var current));
        Assert.Equal(new SystemStatusSnapshot(2, "stopped"), current);
    }

    [Fact]
    public async Task 変わっていなければ送らない()
    {
        _running = true;
        _feed.Attach(() => _running);

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
    public async Task 送れなかったら警告をログに残して続ける()
    {
        _running = true;
        _feed.Attach(() => _running);
        _broadcast.Failure = new IOException("切れた");

        _running = false;
        await _feed.PublishIfChangedAsync(Token);

        // 様子は進めたまま（画面は、次の変化か、つなぎ直したときに最新を受け取る）
        Assert.True(_feed.TryGetCurrent(out var current));
        Assert.Equal(2, current.Version);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("画面", StringComparison.Ordinal));
    }

    private sealed class RecordingBroadcast : IScreenBroadcast
    {
        public List<(string Method, object Payload)> Sent { get; } = [];

        public Exception? Failure { get; set; }

        public Task SendAsync(string method, object payload, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Sent.Add((method, payload));
            return Task.CompletedTask;
        }
    }
}
