using FlowHub.BuildingBlocks;
using FlowHub.Dispatching;
using FlowHub.Map;
using FlowHub.Traffic;
using Microsoft.Extensions.Logging;

namespace FlowHub.Api.Tests;

/// <summary>
/// 「ログに残して画面で知らせる」イベントを、ログに残す購読者のテスト（画面は、画面に取りかかるときに足す）。
/// </summary>
public class WarningLogSubscriptionsTests
{
    private static readonly TimeProvider Time = TimeProvider.System;

    private sealed record LogEntry(LogLevel Level, string Category, string Message);

    /// <summary>出したログを覚えておく。</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Entries.Add(new LogEntry(logLevel, category, formatter(state, exception)));
        }
    }

    private static async Task<LogEntry> PublishAsync(IIntegrationEvent integrationEvent)
    {
        using var provider = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var subscriptions = new EventSubscriptions();
        ApiSubscriptions.Register(subscriptions, factory);
        var bus = new InProcessEventBus(subscriptions);

        await bus.PublishAsync(integrationEvent, TestContext.Current.CancellationToken);
        await bus.FlushAsync(TestContext.Current.CancellationToken);

        return Assert.Single(provider.Entries);
    }

    [Fact]
    public async Task 搬送の中断を警告としてログに残す()
    {
        var metadata = EventMetadata.Start("transport:7", Time);

        var entry = await PublishAsync(new TransportInterrupted(metadata, new TransportId(7), "consumables"));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("7", entry.Message, StringComparison.Ordinal);
        Assert.Contains("consumables", entry.Message, StringComparison.Ordinal);
        Assert.Contains("transport:7", entry.Message, StringComparison.Ordinal);
        Assert.Contains(metadata.EventId.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 予約が取れず止まっていることを警告としてログに残す()
    {
        var entry = await PublishAsync(new MoveBlocked(EventMetadata.Start("transport:7", Time), new AgvId("AGV-1"), new PanelId("T")));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("AGV-1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("T", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 行ける充電ポートがないことを警告としてログに残す()
    {
        var entry = await PublishAsync(new NoChargingPortAvailable(EventMetadata.Start("charge:AGV-1", Time), new AgvId("AGV-1")));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("AGV-1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task マニュアルで並べた動作を捨てたことを警告としてログに残す()
    {
        var entry = await PublishAsync(new ManualStepsDropped(EventMetadata.Start("manual:AGV-1", Time), new AgvId("AGV-1")));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("AGV-1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 停止したときに止める場所がなかったことを警告としてログに残す()
    {
        var entry = await PublishAsync(new NoParkingSpace(EventMetadata.Start("park:AGV-1", Time), new AgvId("AGV-1")));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("AGV-1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 警告のイベントを購読する()
    {
        var subscriptions = new EventSubscriptions();
        using var factory = LoggerFactory.Create(_ => { });

        ApiSubscriptions.Register(subscriptions, factory);

        Assert.Equal([typeof(TransportInterrupted), typeof(MoveBlocked), typeof(NoChargingPortAvailable), typeof(NoParkingSpace), typeof(ManualStepsDropped)], subscriptions.SubscribedEventTypes);
    }
}
