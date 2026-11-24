using Microsoft.EntityFrameworkCore;

namespace FlowHub.Notifications.Infrastructure;

/// <summary>
/// 知らせの DB の入り口。DbContext は外に出さず、DB の設定を受け取って置き場を開く。
/// </summary>
public static class NotificationsDatabase
{
    /// <summary>知らせのスキーマ名（SQLite ではファイル名）。</summary>
    public const string Schema = "notifications";

    /// <summary>
    /// DB に残す置き場を開く。DB を使える状態にしてから、新しいほうから決まった件数を読む。
    /// </summary>
    /// <param name="configure">DB の設定（PostgreSQL か SQLite か、つなぎ先）。</param>
    /// <param name="initialize">DB を使える状態にする（マイグレーションを当てる、テーブルを作る）。</param>
    /// <param name="capacity">残す件数。</param>
    public static async Task<DbNoticeBoard> OpenAsync(
        Action<DbContextOptionsBuilder> configure,
        Func<DbContext, CancellationToken, Task> initialize,
        TimeProvider timeProvider,
        int capacity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var builder = new DbContextOptionsBuilder<NotificationsDbContext>();
        configure(builder);
        var options = builder.Options;

        await using (var context = new NotificationsDbContext(options))
        {
            await initialize(context, cancellationToken).ConfigureAwait(false);
        }

        var board = new DbNoticeBoard(() => new NotificationsDbContext(options), new InMemoryNoticeBoard(timeProvider, capacity));
        await board.LoadAsync(cancellationToken).ConfigureAwait(false);
        return board;
    }
}

/// <summary>
/// 知らせの置き場（DB）。知らせるたびに DB に書き、決まった件数より古いものは DB からも消す。
/// </summary>
/// <remarks>
/// 読むときは、メモリー上の写し（<see cref="InMemoryNoticeBoard"/>）から返す。写しには、DB に書けたものだけを入れる。
/// </remarks>
public sealed class DbNoticeBoard : INoticeBoard
{
    private readonly Func<NotificationsDbContext> _createContext;
    private readonly InMemoryNoticeBoard _memory;

    internal DbNoticeBoard(Func<NotificationsDbContext> createContext, InMemoryNoticeBoard memory)
    {
        _createContext = createContext;
        _memory = memory;
    }

    public IReadOnlyList<Notice> Recent => _memory.Recent;

    public async Task<Notice> PostAsync(string title, string detail, CancellationToken cancellationToken)
    {
        var notice = _memory.Create(title, detail);
        await using (var context = _createContext())
        {
            context.Notices.Add(new NoticeRow { Sequence = notice.Sequence, At = notice.At, Title = notice.Title, Detail = notice.Detail });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 決まった件数より古いものを消す（DB に頼む検索は単純に保つ）
            var oldest = notice.Sequence - _memory.Capacity;
            await context.Notices.Where(n => n.Sequence <= oldest).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        _memory.Keep(notice);
        return notice;
    }

    /// <summary>新しいほうから決まった件数を読み、写しに入れる（開いたとき）。</summary>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var rows = await context.Notices
            .OrderByDescending(n => n.Sequence)
            .Take(_memory.Capacity)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            _memory.Keep(new Notice(row.Sequence, row.At, row.Title, row.Detail));
        }
    }
}
