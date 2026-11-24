using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowHub.Consumables.Infrastructure;

/// <summary>装置。</summary>
internal sealed class DeviceRow
{
    public string Id { get; set; } = string.Empty;

    public string TransferPanel { get; set; } = string.Empty;

    /// <summary>ストッカーの設定をしたか（空の設定も「設定あり」）。</summary>
    public bool HasStockerSetting { get; set; }
}

/// <summary>装置のストッカー（番号は 1 から）。</summary>
internal sealed class DeviceStockerRow
{
    public string DeviceId { get; set; } = string.Empty;

    public int StockerNumber { get; set; }

    public int SlotCount { get; set; }
}

/// <summary>ステーション。</summary>
internal sealed class StationRow
{
    public string Id { get; set; } = string.Empty;

    public string TransferPanel { get; set; } = string.Empty;
}

/// <summary>加工の種類。</summary>
internal sealed class ProcessTypeRow
{
    public string Id { get; set; } = string.Empty;

    public string CarrierType { get; set; } = string.Empty;

    public int CarrierCapacity { get; set; }
}

/// <summary>品種。</summary>
internal sealed class VarietyRow
{
    public string Id { get; set; } = string.Empty;

    public string ProcessTypeId { get; set; } = string.Empty;
}

/// <summary>ストッカーの設定（装置ごと、品種ごとの個数）。</summary>
internal sealed class StockerSettingRow
{
    public string DeviceId { get; set; } = string.Empty;

    public string VarietyId { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>入庫の予定（ID と品種の対応）。</summary>
internal sealed class ExpectedArrivalRow
{
    public string ConsumableId { get; set; } = string.Empty;

    public string VarietyId { get; set; } = string.Empty;
}

/// <summary>
/// 消耗品の管理のコンテキストの DB。スキーマは <see cref="ConsumablesDatabase.Schema"/>。
/// </summary>
internal sealed class ConsumablesDbContext(DbContextOptions<ConsumablesDbContext> options) : DbContext(options)
{
    private const int IdLength = 100;

    public DbSet<DeviceRow> Devices => Set<DeviceRow>();

    public DbSet<DeviceStockerRow> DeviceStockers => Set<DeviceStockerRow>();

    public DbSet<StationRow> Stations => Set<StationRow>();

    public DbSet<ProcessTypeRow> ProcessTypes => Set<ProcessTypeRow>();

    public DbSet<VarietyRow> Varieties => Set<VarietyRow>();

    public DbSet<StockerSettingRow> StockerSettings => Set<StockerSettingRow>();

    public DbSet<ExpectedArrivalRow> ExpectedArrivals => Set<ExpectedArrivalRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ConsumablesDatabase.Schema);

        modelBuilder.Entity<DeviceRow>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).HasMaxLength(IdLength);
            entity.Property(d => d.TransferPanel).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<DeviceStockerRow>(entity =>
        {
            entity.ToTable("device_stockers");
            entity.HasKey(s => new { s.DeviceId, s.StockerNumber });
            entity.Property(s => s.DeviceId).HasMaxLength(IdLength);
            entity.HasOne<DeviceRow>().WithMany().HasForeignKey(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StationRow>(entity =>
        {
            entity.ToTable("stations");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasMaxLength(IdLength);
            entity.Property(s => s.TransferPanel).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<ProcessTypeRow>(entity =>
        {
            entity.ToTable("process_types");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasMaxLength(IdLength);
            entity.Property(p => p.CarrierType).HasMaxLength(IdLength);
        });

        modelBuilder.Entity<VarietyRow>(entity =>
        {
            entity.ToTable("varieties");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Id).HasMaxLength(IdLength);
            entity.Property(v => v.ProcessTypeId).HasMaxLength(IdLength);
            entity.HasOne<ProcessTypeRow>().WithMany().HasForeignKey(v => v.ProcessTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockerSettingRow>(entity =>
        {
            entity.ToTable("stocker_settings");
            entity.HasKey(s => new { s.DeviceId, s.VarietyId });
            entity.Property(s => s.DeviceId).HasMaxLength(IdLength);
            entity.Property(s => s.VarietyId).HasMaxLength(IdLength);
            entity.HasOne<DeviceRow>().WithMany().HasForeignKey(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<VarietyRow>().WithMany().HasForeignKey(s => s.VarietyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpectedArrivalRow>(entity =>
        {
            entity.ToTable("expected_arrivals");
            entity.HasKey(e => e.ConsumableId);
            entity.Property(e => e.ConsumableId).HasMaxLength(IdLength);
            entity.Property(e => e.VarietyId).HasMaxLength(IdLength);
            entity.HasOne<VarietyRow>().WithMany().HasForeignKey(e => e.VarietyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

/// <summary>
/// マイグレーションを作るとき（dotnet ef）にだけ使う。マイグレーションは PostgreSQL 用だけ作るので、PostgreSQL として設定する。
/// </summary>
internal sealed class ConsumablesDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConsumablesDbContext>
{
    public ConsumablesDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ConsumablesDbContext>();
        builder.UseNpgsql(
            "Host=localhost;Database=flowhub",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ConsumablesDatabase.Schema));
        return new ConsumablesDbContext(builder.Options);
    }
}
