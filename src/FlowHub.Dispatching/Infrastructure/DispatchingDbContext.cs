using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowHub.Dispatching.Infrastructure;

/// <summary>AGV の種類（配車が使う、充電の設定値）。登録するときに省略された値は、そのときの既定値で埋めて保存する。</summary>
internal sealed class DispatchingAgvTypeRow
{
    public string Id { get; set; } = string.Empty;

    public int GoChargeBelowPercent { get; set; }

    public int ResumeAtPercent { get; set; }
}

/// <summary>AGV（配車が使う、キャリアの種類）。キャリアは AGV に固定。</summary>
internal sealed class DispatchingAgvRow
{
    public string Id { get; set; } = string.Empty;

    public string TypeId { get; set; } = string.Empty;

    public string CarrierType { get; set; } = string.Empty;
}

/// <summary>システム全体の充電の既定値（1行だけ）。</summary>
internal sealed class ChargingDefaultsRow
{
    public const int SingleId = 1;

    public int Id { get; set; } = SingleId;

    public int GoChargeBelowPercent { get; set; }

    public int ResumeAtPercent { get; set; }
}

/// <summary>
/// 配車のコンテキストの DB。スキーマは <see cref="DispatchingDatabase.Schema"/>。
/// </summary>
internal sealed class DispatchingDbContext(DbContextOptions<DispatchingDbContext> options) : DbContext(options)
{
    public DbSet<DispatchingAgvTypeRow> AgvTypes => Set<DispatchingAgvTypeRow>();

    public DbSet<DispatchingAgvRow> Agvs => Set<DispatchingAgvRow>();

    public DbSet<ChargingDefaultsRow> ChargingDefaults => Set<ChargingDefaultsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DispatchingDatabase.Schema);

        modelBuilder.Entity<DispatchingAgvTypeRow>(entity =>
        {
            entity.ToTable("agv_types");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).HasMaxLength(100);
        });

        modelBuilder.Entity<DispatchingAgvRow>(entity =>
        {
            entity.ToTable("agvs");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasMaxLength(100);
            entity.Property(a => a.TypeId).HasMaxLength(100);
            entity.Property(a => a.CarrierType).HasMaxLength(100);
            entity.HasOne<DispatchingAgvTypeRow>().WithMany().HasForeignKey(a => a.TypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChargingDefaultsRow>(entity =>
        {
            entity.ToTable("charging_defaults");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedNever();
        });
    }
}

/// <summary>
/// マイグレーションを作るとき（dotnet ef）にだけ使う。マイグレーションは PostgreSQL 用だけ作るので、PostgreSQL として設定する。
/// </summary>
internal sealed class DispatchingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DispatchingDbContext>
{
    public DispatchingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<DispatchingDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Database=flowhub",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DispatchingDatabase.Schema));
        return new DispatchingDbContext(builder.Options);
    }
}
