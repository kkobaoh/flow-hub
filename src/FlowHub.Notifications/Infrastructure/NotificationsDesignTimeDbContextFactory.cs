using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowHub.Notifications.Infrastructure;

/// <summary>
/// マイグレーションを作るとき（dotnet ef）にだけ使う。マイグレーションは PostgreSQL 用だけ作るので、PostgreSQL として設定する。
/// DB にはつながない（つなぎ先は使わない）。
/// </summary>
internal sealed class NotificationsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NotificationsDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Database=flowhub",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDatabase.Schema));
        return new NotificationsDbContext(builder.Options);
    }
}
