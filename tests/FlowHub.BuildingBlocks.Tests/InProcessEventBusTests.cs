namespace FlowHub.BuildingBlocks.Tests;

public class InProcessEventBusTests
{
    private sealed record SomethingHappened(EventMetadata Metadata, string Name) : IIntegrationEvent;

    private sealed record OtherHappened(EventMetadata Metadata) : IIntegrationEvent;

    /// <summary>受け取ったイベントを記録するハンドラー。</summary>
    private sealed class Recorder<TEvent>(List<string> log, string name) : IEventSubscriber<TEvent>
        where TEvent : IIntegrationEvent
    {
        public Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken)
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static EventMetadata Metadata(string correlationId = "transport-1") =>
        EventMetadata.Start(correlationId, new FixedTimeProvider(Now));

    [Fact]
    public async Task 公開したイベントはFlushするまで届けない()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "A"));
        var bus = new InProcessEventBus(subscriptions);

        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);

        Assert.Empty(log);
    }

    [Fact]
    public async Task Flushすると購読したハンドラーに登録した順に届ける()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "A"));
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "B"));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);

        await bus.FlushAsync(CancellationToken.None);

        Assert.Equal(["A", "B"], log);
    }

    [Fact]
    public async Task 購読していない種類のイベントは届けない()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "A"));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new OtherHappened(Metadata()), CancellationToken.None);

        await bus.FlushAsync(CancellationToken.None);

        Assert.Empty(log);
    }

    [Fact]
    public async Task 公開した順に届ける()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "something"));
        subscriptions.Subscribe(() => new Recorder<OtherHappened>(log, "other"));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new OtherHappened(Metadata()), CancellationToken.None);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);

        await bus.FlushAsync(CancellationToken.None);

        Assert.Equal(["other", "something"], log);
    }

    [Fact]
    public async Task 一度届けたイベントはもう一度Flushしても届けない()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "A"));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);
        await bus.FlushAsync(CancellationToken.None);

        await bus.FlushAsync(CancellationToken.None);

        Assert.Equal(["A"], log);
    }

    [Fact]
    public async Task ハンドラーが失敗してもほかのハンドラーには届けてから失敗を返す()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe<SomethingHappened>(() => new Failing());
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "B"));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);

        await Assert.ThrowsAsync<AggregateException>(() => bus.FlushAsync(CancellationToken.None));

        Assert.Equal(["B"], log);
    }

    [Fact]
    public async Task 取り消されたハンドラーも失敗として扱いほかのハンドラーには届ける()
    {
        var log = new List<string>();
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe<SomethingHappened>(() => new Canceling());
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>(log, "B"));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);

        var error = await Assert.ThrowsAsync<AggregateException>(() => bus.FlushAsync(CancellationToken.None));

        Assert.IsType<TaskCanceledException>(Assert.Single(error.InnerExceptions));
        Assert.Equal(["B"], log);
    }

    [Fact]
    public async Task 失敗したハンドラーの例外をそのまま返す()
    {
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe<SomethingHappened>(() => new Failing());
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "x"), CancellationToken.None);

        var error = await Assert.ThrowsAsync<AggregateException>(() => bus.FlushAsync(CancellationToken.None));

        Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
    }

    private sealed class Canceling : IEventSubscriber<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) =>
            Task.FromCanceled(new CancellationToken(canceled: true));
    }

    private sealed class Failing : IEventSubscriber<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("失敗");
    }

    [Fact]
    public async Task 届けるたびに流れのIDときっかけのIDを付けたトレースを残す()
    {
        var activities = new List<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "FlowHub.Events",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>([], "A"));
        var bus = new InProcessEventBus(subscriptions);
        var metadata = Metadata("transport-7");
        await bus.PublishAsync(new SomethingHappened(metadata, "x"), CancellationToken.None);

        await bus.FlushAsync(CancellationToken.None);

        var activity = Assert.Single(activities);
        Assert.Equal("transport-7", activity.GetTagItem("flowhub.correlation_id"));
        Assert.Equal(metadata.EventId, activity.GetTagItem("flowhub.event_id"));
    }

    /// <summary>届け始めたことを知らせ、続けてよいと言われるまで待つハンドラー。</summary>
    private sealed class Blocking(List<string> log, TaskCompletionSource started, Task release) : IEventSubscriber<SomethingHappened>
    {
        public async Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            lock (log)
            {
                log.Add($"{integrationEvent.Name} 開始");
            }

            if (integrationEvent.Name == "1")
            {
                started.SetResult();
                await release;
            }

            lock (log)
            {
                log.Add($"{integrationEvent.Name} 終了");
            }
        }
    }

    [Fact]
    public async Task 届けている途中に別のFlushが呼ばれても公開した順に1つずつ届ける()
    {
        var log = new List<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Blocking(log, started, release.Task));
        var bus = new InProcessEventBus(subscriptions);
        await bus.PublishAsync(new SomethingHappened(Metadata(), "1"), TestContext.Current.CancellationToken);
        var first = bus.FlushAsync(TestContext.Current.CancellationToken);
        await started.Task;

        await bus.PublishAsync(new SomethingHappened(Metadata(), "2"), TestContext.Current.CancellationToken);
        await bus.FlushAsync(TestContext.Current.CancellationToken); // 1 を届けている途中（2 は、届けている側があとで届ける）
        release.SetResult();
        await first;

        Assert.Equal(["1 開始", "1 終了", "2 開始", "2 終了"], log);
    }

    [Fact]
    public async Task 複数のスレッドから同時に公開しても全部届ける()
    {
        var count = 0;
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Counting(() => Interlocked.Increment(ref count)));
        var bus = new InProcessEventBus(subscriptions);

        await Parallel.ForAsync(0, 10_000, TestContext.Current.CancellationToken, async (i, cancellationToken) =>
            await bus.PublishAsync(new SomethingHappened(Metadata(), $"{i}"), cancellationToken));
        await bus.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(10_000, count);
    }

    private sealed class Counting(Action onHandled) : IEventSubscriber<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken)
        {
            onHandled();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void 購読しているイベントの種類を登録した順に重複なく一覧できる()
    {
        var subscriptions = new EventSubscriptions();
        subscriptions.Subscribe(() => new Recorder<OtherHappened>([], "A"));
        subscriptions.Subscribe(() => new Recorder<SomethingHappened>([], "B"));
        subscriptions.Subscribe(() => new Recorder<OtherHappened>([], "C"));

        Assert.Equal([typeof(OtherHappened), typeof(SomethingHappened)], subscriptions.SubscribedEventTypes);
    }

    // ---- メタデータ ----

    [Fact]
    public void 流れの始まりのイベントはきっかけを持たない()
    {
        var metadata = EventMetadata.Start("transport-1", new FixedTimeProvider(Now));

        Assert.NotEqual(Guid.Empty, metadata.EventId);
        Assert.Equal("transport-1", metadata.CorrelationId);
        Assert.Null(metadata.CausationId);
        Assert.Equal(Now, metadata.OccurredAt);
    }

    [Fact]
    public void きっかけのあるイベントは同じ流れのIDときっかけのIDを持つ()
    {
        var cause = EventMetadata.Start("transport-1", new FixedTimeProvider(Now));

        var metadata = EventMetadata.CausedBy(cause, new FixedTimeProvider(Now.AddSeconds(1)));

        Assert.NotEqual(cause.EventId, metadata.EventId);
        Assert.Equal("transport-1", metadata.CorrelationId);
        Assert.Equal(cause.EventId, metadata.CausationId);
        Assert.Equal(Now.AddSeconds(1), metadata.OccurredAt);
    }

    [Fact]
    public void 流れのIDは空にできない()
    {
        Assert.Throws<ArgumentException>(() => EventMetadata.Start(" ", new FixedTimeProvider(Now)));
    }
}
