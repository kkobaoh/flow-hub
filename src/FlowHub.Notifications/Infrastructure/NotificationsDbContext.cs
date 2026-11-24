using Microsoft.EntityFrameworkCore;

namespace FlowHub.Notifications.Infrastructure;

/// <summary>残している知らせ。</summary>
internal sealed class NoticeRow
{
    public long Sequence { get; set; }

    /// <summary>知らせた時刻（UTC）。</summary>
    public DateTimeOffset At { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// 知らせの DB。スキーマは <see cref="NotificationsDatabase.Schema"/>。
/// </summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<NoticeRow> Notices => Set<NoticeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(NotificationsDatabase.Schema);

        modelBuilder.Entity<NoticeRow>(entity =>
        {
            entity.ToTable("notices");
            entity.HasKey(n => n.Sequence);
            entity.Property(n => n.Sequence).ValueGeneratedNever();
            entity.Property(n => n.Title).HasMaxLength(200);
            entity.Property(n => n.Detail).HasMaxLength(1000);
        });
    }
}
