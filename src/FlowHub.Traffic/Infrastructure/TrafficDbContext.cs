using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowHub.Traffic.Infrastructure;

/// <summary>AGV の種類（交通整理が使う、動きの速さ）。</summary>
internal sealed class AgvTypeRow
{
    public string Id { get; set; } = string.Empty;

    public double ForwardSpeedMmPerSecond { get; set; }

    public double BackwardSpeedMmPerSecond { get; set; }

    public double RotationSpeedDegreesPerSecond { get; set; }
}

/// <summary>AGV。</summary>
internal sealed class AgvRow
{
    public string Id { get; set; } = string.Empty;

    public string TypeId { get; set; } = string.Empty;
}

/// <summary>AGV ごとの、次に付けるコマンドの番号。</summary>
internal sealed class CommandNumberRow
{
    public string AgvId { get; set; } = string.Empty;

    public long NextNumber { get; set; }
}

/// <summary>
/// 交通整理のコンテキストの DB。スキーマは <see cref="TrafficDatabase.Schema"/>。
/// </summary>
internal sealed class TrafficDbContext(DbContextOptions<TrafficDbContext> options) : DbContext(options)
{
    public DbSet<AgvTypeRow> AgvTypes => Set<AgvTypeRow>();

    public DbSet<AgvRow> Agvs => Set<AgvRow>();

    public DbSet<CommandNumberRow> CommandNumbers => Set<CommandNumberRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TrafficDatabase.Schema);

        modelBuilder.Entity<AgvTypeRow>(entity =>
        {
            entity.ToTable("agv_types");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasMaxLength(100);
        });

        modelBuilder.Entity<AgvRow>(entity =>
        {
            entity.ToTable("agvs");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasMaxLength(100);
            entity.Property(a => a.TypeId).HasMaxLength(100);
            entity.HasOne<AgvTypeRow>().WithMany().HasForeignKey(a => a.TypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommandNumberRow>(entity =>
        {
            entity.ToTable("command_numbers");
            entity.HasKey(n => n.AgvId);
            entity.Property(n => n.AgvId).HasMaxLength(100);
        });
    }
}

/// <summary>
/// マイグレーションを作るとき（dotnet ef）にだけ使う。マイグレーションは PostgreSQL 用だけ作るので、PostgreSQL として設定する。
/// </summary>
internal sealed class TrafficDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrafficDbContext>
{
    public TrafficDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<TrafficDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Database=flowhub",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TrafficDatabase.Schema));
        return new TrafficDbContext(builder.Options);
    }
}
