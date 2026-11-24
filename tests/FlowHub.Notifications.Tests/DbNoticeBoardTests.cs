using FlowHub.Notifications.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Notifications.Tests;

/// <summary>
/// 知らせを DB に残す置き場のテスト。メモリ上の SQLite を使う（同じ接続を開いている間だけ中身が残る）。
/// </summary>
public sealed class DbNoticeBoardTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public DbNoticeBoardTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>置き場を開く。同じ接続を使うので、開き直すと「次に起動したとき」と同じになる。</summary>
    private Task<DbNoticeBoard> OpenAsync(int capacity) =>
        NotificationsDatabase.OpenAsync(
            builder => builder.UseSqlite(_connection),
            (context, cancellationToken) => context.Database.EnsureCreatedAsync(cancellationToken),
            new SettableTimeProvider(T0),
            capacity,
            Token);

    [Fact]
    public async Task 残した知らせは開き直しても残り番号は続きから付ける()
    {
        var board = await OpenAsync(capacity: 10);
        await board.PostAsync("搬送を中断しました", "搬送 #11", Token);
        await board.PostAsync("行ける充電ポートがありません", "AGV-2", Token);

        var reopened = await OpenAsync(capacity: 10);
        var third = await reopened.PostAsync("予約が取れずに止まっています", "AGV-1", Token);

        Assert.Equal(3, third.Sequence);
        Assert.Equal(["予約が取れずに止まっています", "行ける充電ポートがありません", "搬送を中断しました"], reopened.Recent.Select(n => n.Title));
        Assert.Equal("搬送 #11", reopened.Recent[2].Detail);
        Assert.Equal(T0, reopened.Recent[2].At);
    }

    [Fact]
    public async Task 決まった件数より古いものはDBからも消す()
    {
        var board = await OpenAsync(capacity: 2);
        for (var i = 1; i <= 3; i++)
        {
            await board.PostAsync($"知らせ{i}", string.Empty, Token);
        }

        // 件数を増やして開き直しても、消したものは戻らない
        var reopened = await OpenAsync(capacity: 10);

        Assert.Equal(["知らせ3", "知らせ2"], reopened.Recent.Select(n => n.Title));
    }
}
