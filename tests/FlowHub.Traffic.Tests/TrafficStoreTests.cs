using FlowHub.BuildingBlocks;
using FlowHub.Map;
using FlowHub.Routing;
using FlowHub.Traffic.Application;
using FlowHub.Traffic.Domain;
using FlowHub.Traffic.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Traffic.Tests;

/// <summary>
/// 交通整理の DB（AGV の種類、AGV、コマンドの番号）のテスト。メモリ上の SQLite を使う。
/// </summary>
public sealed class TrafficStoreTests : IDisposable
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly MotionProfile Motion = new(500, 250, 90);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TrafficStoreTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class IgnoringPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent => Task.CompletedTask;
    }

    /// <summary>同じ接続を使うので、開き直すと「次に起動したとき」と同じになる。</summary>
    private Task<TrafficStore> OpenAsync() =>
        TrafficDatabase.OpenAsync(
            builder => builder.UseSqlite(_connection),
            (context, cancellationToken) => context.Database.EnsureCreatedAsync(cancellationToken),
            Token);

    private static InMemoryMapStore Map()
    {
        var map = new InMemoryMapStore();
        map.Publish(MapSnapshot.Create([new Panel(new PanelId("A"), PanelType.Normal, false)], []).Snapshot!);
        return map;
    }

    private static TrafficService Service(ICommandNumberStore numbers)
    {
        var service = new TrafficService(Map(), new IgnoringPublisher(), new FixedTimeProvider(), numbers);
        service.RegisterAgv(Agv, Motion);
        return service;
    }

    // ---- コマンドの番号 ----

    [Fact]
    public async Task コマンドの番号は再起動しても続きから数える()
    {
        var store = await OpenAsync();
        using (var first = Service(store))
        {
            await first.ResetPositionAsync(Agv, new Pose("A", 0), Token);
            Assert.Equal(1, Assert.Single(await first.HandleCommandRequestAsync(Agv, -1, Token)).Number);
        }

        using var second = Service(await OpenAsync());
        await second.ResetPositionAsync(Agv, new Pose("A", 0), Token);

        Assert.Equal(2, Assert.Single(await second.HandleCommandRequestAsync(Agv, -1, Token)).Number);
    }

    [Fact]
    public async Task まだ番号を付けたことのないAGVは1から数える()
    {
        var store = await OpenAsync();

        Assert.Equal(1, store.NextNumberOf(Agv));
    }

    [Fact]
    public void DBを使わないときは番号を覚えない()
    {
        var numbers = new InMemoryCommandNumberStore();
        numbers.Save(Agv, 5);

        Assert.Equal(5, numbers.NextNumberOf(Agv));
        Assert.Equal(1, new InMemoryCommandNumberStore().NextNumberOf(Agv));
    }

    // ---- AGV の種類と AGV ----

    [Fact]
    public async Task 登録したAGVを種類の速さと一緒に読む()
    {
        var store = await OpenAsync();
        await store.AddAgvTypeAsync("small", Motion, Token);
        await store.AddAgvAsync(Agv, "small", Token);

        var agvs = await (await OpenAsync()).LoadAgvsAsync(Token);

        Assert.Equal([new AgvConfiguration(Agv, Motion)], agvs);
    }

    [Fact]
    public async Task 何も登録していなければ空()
    {
        var store = await OpenAsync();

        Assert.True(await store.IsEmptyAsync(Token));
        Assert.Empty(await store.LoadAgvsAsync(Token));
    }

    [Fact]
    public async Task 登録されていない種類のAGVは登録できない()
    {
        var store = await OpenAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAgvAsync(Agv, "unknown", Token));
    }
}
