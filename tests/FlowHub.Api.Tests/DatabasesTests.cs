using FlowHub.Api.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FlowHub.Api.Tests;

/// <summary>
/// DB の種類（PostgreSQL と SQLite）の切り替えのテスト。
/// </summary>
public sealed class DatabasesTests : IDisposable
{
    private const string PostgreSqlConnection = "Host=localhost;Database=flowhub;Username=flowhub;Password=flowhub";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flowhub-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // SQLite のファイルを開いたままにしないよう、接続のプールを空にしてから消す
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>テストのための、スキーマ「map」を使うコンテキスト。</summary>
    private sealed class MapLikeDbContext(DbContextOptions<MapLikeDbContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("map");
    }

    /// <summary>テストのための、スキーマ「traffic」を使うコンテキスト。</summary>
    private sealed class TrafficLikeDbContext(DbContextOptions<TrafficLikeDbContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("traffic");
    }

    private static MapLikeDbContext Map(DatabaseOptions options)
    {
        var builder = new DbContextOptionsBuilder<MapLikeDbContext>();
        Databases.Configure(builder, options, "map");
        return new MapLikeDbContext(builder.Options);
    }

    private static TrafficLikeDbContext Traffic(DatabaseOptions options)
    {
        var builder = new DbContextOptionsBuilder<TrafficLikeDbContext>();
        Databases.Configure(builder, options, "traffic");
        return new TrafficLikeDbContext(builder.Options);
    }

    // ---- SQLite ----

    [Fact]
    public async Task SQLiteではコンテキストごとに別のファイルにテーブルを作る()
    {
        var options = new DatabaseOptions { Provider = DatabaseProvider.Sqlite, SqliteDirectory = _directory };
        await using var map = Map(options);
        await using var traffic = Traffic(options);

        await Databases.InitializeAsync(map, options, TestContext.Current.CancellationToken);
        await Databases.InitializeAsync(traffic, options, TestContext.Current.CancellationToken);
        map.Items.Add(new Item { Name = "地図" });
        await map.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_directory, "map.db")));
        Assert.True(File.Exists(Path.Combine(_directory, "traffic.db")));
        Assert.Equal(["地図"], await map.Items.Select(i => i.Name).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await traffic.Items.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SQLiteでは一度作ったテーブルの中身を次に開いたときも読める()
    {
        var options = new DatabaseOptions { Provider = DatabaseProvider.Sqlite, SqliteDirectory = _directory };
        await using (var first = Map(options))
        {
            await Databases.InitializeAsync(first, options, TestContext.Current.CancellationToken);
            first.Items.Add(new Item { Name = "残る" });
            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var second = Map(options);
        await Databases.InitializeAsync(second, options, TestContext.Current.CancellationToken);

        Assert.Equal(["残る"], await second.Items.Select(i => i.Name).ToListAsync(TestContext.Current.CancellationToken));
    }

    // ---- PostgreSQL ----

    [Fact]
    public void PostgreSQLではコンテキストのスキーマにテーブルを作る()
    {
        var options = new DatabaseOptions { Provider = DatabaseProvider.PostgreSql, ConnectionString = PostgreSqlConnection };
        using var map = Map(options);

        var script = map.Database.GenerateCreateScript();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", map.Database.ProviderName);
        Assert.Contains("CREATE SCHEMA map;", script, StringComparison.Ordinal);
        Assert.Contains("map.\"Items\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSQLではマイグレーションの履歴もコンテキストのスキーマに置く()
    {
        var options = new DatabaseOptions { Provider = DatabaseProvider.PostgreSql, ConnectionString = PostgreSqlConnection };
        using var map = Map(options);

        var script = map.GetService<IHistoryRepository>().GetCreateScript();

        Assert.Contains("map.\"__EFMigrationsHistory\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSQLで接続文字列がなければエラーにする()
    {
        var options = new DatabaseOptions { Provider = DatabaseProvider.PostgreSql };

        Assert.Throws<InvalidOperationException>(() => Map(options));
    }
}
