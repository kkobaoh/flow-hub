using Microsoft.EntityFrameworkCore;

namespace FlowHub.Map.Infrastructure;

/// <summary>公開したマップの版。</summary>
internal sealed class MapVersionRow
{
    public int Version { get; set; }
}

/// <summary>版ごとのパネル。</summary>
internal sealed class PanelRow
{
    public int Version { get; set; }

    /// <summary>版の中での並び順。</summary>
    public int Order { get; set; }

    public string PanelId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool HasChargingPin { get; set; }

    /// <summary>キャリアを下ろす角度（特殊消耗品受け渡しパネルだけ）。</summary>
    public int? CarrierLoweringHeading { get; set; }
}

/// <summary>版ごとのつながり。</summary>
internal sealed class ConnectionRow
{
    public int Version { get; set; }

    /// <summary>版の中での並び順。</summary>
    public int Order { get; set; }

    public string FromPanelId { get; set; } = string.Empty;

    public string ToPanelId { get; set; } = string.Empty;

    public int DistanceMm { get; set; }

    public int HeadingDegrees { get; set; }

    public bool OneWay { get; set; }
}

/// <summary>受け渡しパネルごとのキャリアを下げる高さ（版とは別に持つ）。</summary>
internal sealed class CarrierHeightRow
{
    public string PanelId { get; set; } = string.Empty;

    public int HeightMm { get; set; }

    /// <summary>設定した時刻（UTC）。</summary>
    public DateTimeOffset SetAt { get; set; }
}

/// <summary>
/// マップのコンテキストの DB。スキーマは <see cref="MapDatabase.Schema"/>。
/// </summary>
internal sealed class MapDbContext(DbContextOptions<MapDbContext> options) : DbContext(options)
{
    public DbSet<MapVersionRow> Versions => Set<MapVersionRow>();

    public DbSet<PanelRow> Panels => Set<PanelRow>();

    public DbSet<ConnectionRow> Connections => Set<ConnectionRow>();

    public DbSet<CarrierHeightRow> CarrierHeights => Set<CarrierHeightRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MapDatabase.Schema);

        modelBuilder.Entity<MapVersionRow>(entity =>
        {
            entity.ToTable("versions");
            entity.HasKey(v => v.Version);
            entity.Property(v => v.Version).ValueGeneratedNever();
        });

        modelBuilder.Entity<PanelRow>(entity =>
        {
            entity.ToTable("panels");
            entity.HasKey(p => new { p.Version, p.PanelId });
            entity.Property(p => p.PanelId).HasMaxLength(100);
            entity.Property(p => p.Type).HasMaxLength(20);
        });

        modelBuilder.Entity<ConnectionRow>(entity =>
        {
            entity.ToTable("connections");
            entity.HasKey(c => new { c.Version, c.Order });
            entity.Property(c => c.FromPanelId).HasMaxLength(100);
            entity.Property(c => c.ToPanelId).HasMaxLength(100);
        });

        modelBuilder.Entity<CarrierHeightRow>(entity =>
        {
            entity.ToTable("carrier_heights");
            entity.HasKey(h => h.PanelId);
            entity.Property(h => h.PanelId).HasMaxLength(100);
        });
    }
}
