using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowHub.Map.Infrastructure;

/// <summary>
/// マイグレーションを作るとき（dotnet ef）にだけ使う。マイグレーションは PostgreSQL 用だけ作るので、PostgreSQL として設定する。
/// DB にはつながない（つなぎ先は使わない）。
/// </summary>
internal sealed class MapDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MapDbContext>
{
    public MapDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MapDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Database=flowhub",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MapDatabase.Schema));
        return new MapDbContext(builder.Options);
    }
}
