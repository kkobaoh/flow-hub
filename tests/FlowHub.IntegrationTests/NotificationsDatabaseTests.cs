using FlowHub.Api.Database;
using FlowHub.Notifications.Infrastructure;

namespace FlowHub.IntegrationTests;

/// <summary>
/// 画面で知らせるメッセージの DB を、ローカルの PostgreSQL で確かめる（マイグレーションを当て、残して読み直す）。
/// </summary>
public class NotificationsDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>決まった時刻を返す（PostgreSQL の日時の列は UTC しか受け付けないので、UTC 以外の時刻で確かめる）。</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 24, 18, 0, 0, TimeSpan.FromHours(9));
    }

    private static Task<DbNoticeBoard> OpenAsync(DatabaseOptions options, int capacity) =>
        NotificationsDatabase.OpenAsync(
            builder => Databases.Configure(builder, options, NotificationsDatabase.Schema),
            (context, cancellationToken) => Databases.InitializeAsync(context, options, cancellationToken),
            new FixedTimeProvider(),
            capacity,
            Token);

    [Fact]
    public async Task マイグレーションを当ててメッセージを残し決まった件数だけ読み直せる()
    {
        var options = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(options, NotificationsDatabase.Schema, Token);

        var board = await OpenAsync(options, capacity: 2);
        for (var i = 1; i <= 3; i++)
        {
            await board.PostAsync($"知らせ{i}", $"中身{i}", Token);
        }

        var reopened = await OpenAsync(options, capacity: 10);

        Assert.Equal([3L, 2L], reopened.Recent.Select(n => n.Sequence));
        Assert.Equal("中身3", reopened.Recent[0].Detail);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero), reopened.Recent[0].At);
    }
}
