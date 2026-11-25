using Microsoft.EntityFrameworkCore;

namespace FlowHub.Map.Infrastructure;

/// <summary>公開したマップの版。</summary>
internal sealed class MapVersionRow
{
    public int Version { get; set; }

    /// <summary>パネルの一辺の長さ（mm）。</summary>
    public int PanelSizeMm { get; set; }
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

    /// <summary>格子の列（東へ増える）。</summary>
    public int Column { get; set; }

    /// <summary>格子の行（南へ増える）。</summary>
    public int Row { get; set; }

    /// <summary>十字のずれ（mm、東が正）。</summary>
    public int CrossOffsetEastMm { get; set; }

    /// <summary>十字のずれ（mm、南が正）。</summary>
    public int CrossOffsetSouthMm { get; set; }

    /// <summary>下にあるものの外形（受け渡しパネルだけ。なければ 4 つとも null）。</summary>
    public int? OutlineXMm { get; set; }

    public int? OutlineYMm { get; set; }

    public int? OutlineWidthMm { get; set; }

    public int? OutlineHeightMm { get; set; }
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

/// <summary>マップの下書き（1行だけ。Id はいつも 1）。中身は検証しないので、JSON のまま持つ。</summary>
internal sealed class MapDraftRow
{
    public const int SingleId = 1;

    public int Id { get; set; }

    /// <summary>下書きの番号。保存するたびと消すたびに進む。</summary>
    public int Revision { get; set; }

    /// <summary>保存した時刻（UTC）。下書きがなければ null。</summary>
    public DateTimeOffset? SavedAt { get; set; }

    /// <summary>下書きの中身（JSON）。下書きがなければ null。</summary>
    public string? Content { get; set; }
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

    public DbSet<MapDraftRow> Drafts => Set<MapDraftRow>();

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

        modelBuilder.Entity<MapDraftRow>(entity =>
        {
            entity.ToTable("drafts");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedNever();
        });
    }
}
