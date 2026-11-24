using Microsoft.EntityFrameworkCore;

namespace FlowHub.Map.Infrastructure;

/// <summary>
/// マップのコンテキストの DB の入り口。DbContext は外に出さず、DB の設定を受け取ってマップ置き場を開く。
/// </summary>
public static class MapDatabase
{
    /// <summary>マップのコンテキストのスキーマ名（SQLite ではファイル名）。</summary>
    public const string Schema = "map";

    /// <summary>
    /// DB に保存するマップ置き場を開く。DB を使える状態にしてから、一番新しい版とキャリアの高さを読む。
    /// </summary>
    /// <param name="configure">DB の設定（PostgreSQL か SQLite か、つなぎ先）。</param>
    /// <param name="initialize">DB を使える状態にする（マイグレーションを当てる、テーブルを作る）。</param>
    public static async Task<DbMapStore> OpenAsync(
        Action<DbContextOptionsBuilder> configure,
        Func<DbContext, CancellationToken, Task> initialize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(initialize);

        var builder = new DbContextOptionsBuilder<MapDbContext>();
        configure(builder);
        var options = builder.Options;

        await using (var context = new MapDbContext(options))
        {
            await initialize(context, cancellationToken).ConfigureAwait(false);
        }

        var store = new DbMapStore(() => new MapDbContext(options));
        await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return store;
    }
}

/// <summary>
/// マップの保存場所（DB）。公開した版はすべて番号を付けて残し、開いたときは一番新しい版を使う。
/// </summary>
/// <remarks>
/// 読むときは、メモリー上の写し（<see cref="InMemoryMapStore"/>）から返す。公開とキャリアの高さの設定は、そのつど DB に書く。
/// </remarks>
public sealed class DbMapStore : IMapStore
{
    private readonly Func<MapDbContext> _createContext;
    private readonly InMemoryMapStore _memory = new();
    private readonly Lock _lock = new();
    private int _latestVersion;

    /// <summary>
    /// 読み直すときの検証のオプション。交点の間の距離は、公開したときに、そのときの設定で確かめてあるので、設定が変わっても読み直せるように確かめない（最小の距離を 1mm にする）。
    /// </summary>
    private static readonly MapValidationOptions ReloadOptions = new() { MinimumCrossDistanceMm = 1 };

    internal DbMapStore(Func<MapDbContext> createContext) => _createContext = createContext;

    public MapSnapshot? Current => _memory.Current;

    public int CurrentVersion => _memory.CurrentVersion;

    public int? CarrierHeightOf(PanelId panel) => _memory.CarrierHeightOf(panel);

    /// <summary>
    /// マップを公開する。次の番号の版として DB に保存してから、今の版にする。DB に残す版は、描く情報を必ず持つ。
    /// </summary>
    public void Publish(MapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var drawing = snapshot.Drawing ?? throw new ArgumentException("描く情報（パネルの位置）のないマップは、DB に残せません。", nameof(snapshot));
        lock (_lock)
        {
            var version = _latestVersion + 1;
            using (var context = _createContext())
            {
                context.Versions.Add(new MapVersionRow { Version = version, PanelSizeMm = drawing.PanelSizeMm });
                context.Panels.AddRange(snapshot.Panels.Select((panel, order) => ToRow(version, order, panel, drawing.Placements[panel.Id])));
                context.Connections.AddRange(snapshot.Connections.Select((connection, order) => new ConnectionRow
                {
                    Version = version,
                    Order = order,
                    FromPanelId = connection.From.Value,
                    ToPanelId = connection.To.Value,
                    DistanceMm = connection.DistanceMm,
                    HeadingDegrees = connection.Heading.Degrees,
                    OneWay = connection.OneWay,
                }));
                context.SaveChanges();
            }

            _latestVersion = version;
            _memory.Publish(snapshot, version);
        }
    }

    private static PanelRow ToRow(int version, int order, Panel panel, PanelPlacement placement) => new()
    {
        Version = version,
        Order = order,
        PanelId = panel.Id.Value,
        Type = panel.Type.ToString(),
        HasChargingPin = panel.HasChargingPin,
        CarrierLoweringHeading = panel.CarrierLoweringHeading?.Degrees,
        Column = placement.Position.Column,
        Row = placement.Position.Row,
        CrossOffsetEastMm = placement.CrossOffset.EastMm,
        CrossOffsetSouthMm = placement.CrossOffset.SouthMm,
        OutlineXMm = placement.Outline?.XMm,
        OutlineYMm = placement.Outline?.YMm,
        OutlineWidthMm = placement.Outline?.WidthMm,
        OutlineHeightMm = placement.Outline?.HeightMm,
    };

    private static PanelPlacement ToPlacement(PanelRow row) => new(
        new GridPosition(row.Column, row.Row),
        new CrossOffset(row.CrossOffsetEastMm, row.CrossOffsetSouthMm),
        row is { OutlineXMm: { } x, OutlineYMm: { } y, OutlineWidthMm: { } width, OutlineHeightMm: { } height } ? new Outline(x, y, width, height) : null);

    /// <summary>
    /// キャリアの高さを設定する。新しく設定されたほうを使い、使うことにしたら DB に書く。
    /// </summary>
    public void SetCarrierHeight(PanelId panel, int heightMm, DateTimeOffset setAt)
    {
        lock (_lock)
        {
            if (!_memory.TrySetCarrierHeight(panel, heightMm, setAt))
            {
                return; // すでに新しい設定がある
            }

            using var context = _createContext();
            var row = context.CarrierHeights.Find(panel.Value);
            if (row is null)
            {
                context.CarrierHeights.Add(new CarrierHeightRow { PanelId = panel.Value, HeightMm = heightMm, SetAt = setAt.ToUniversalTime() });
            }
            else
            {
                row.HeightMm = heightMm;
                row.SetAt = setAt.ToUniversalTime();
            }

            context.SaveChanges();
        }
    }

    /// <summary>保存してある版の番号（小さい順）。</summary>
    internal async Task<IReadOnlyList<int>> GetPublishedVersionsAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        return await context.Versions.Select(v => v.Version).OrderBy(v => v).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 一番新しい版と、キャリアの高さを DB から読む。
    /// </summary>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var latest = await context.Versions.OrderByDescending(v => v.Version).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return;
        }

        var version = latest.Version;

        var panels = await context.Panels.Where(p => p.Version == version).OrderBy(p => p.Order).ToListAsync(cancellationToken).ConfigureAwait(false);
        var connections = await context.Connections.Where(c => c.Version == version).OrderBy(c => c.Order).ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = MapSnapshot.Create(
            panels.Select(p => new Panel(new PanelId(p.PanelId), Enum.Parse<PanelType>(p.Type), p.HasChargingPin)
            {
                CarrierLoweringHeading = p.CarrierLoweringHeading is { } heading ? new Heading(heading) : null,
            }),
            connections.Select(c => new Connection(new PanelId(c.FromPanelId), new PanelId(c.ToPanelId), c.DistanceMm, new Heading(c.HeadingDegrees), c.OneWay)),
            ReloadOptions,
            new MapDrawing(latest.PanelSizeMm, panels.ToDictionary(p => new PanelId(p.PanelId), ToPlacement)));
        var snapshot = result.Snapshot
            ?? throw new InvalidOperationException($"保存してあるマップの版 {version} が、検証を通りません：{string.Join("、", result.Errors.Select(e => e.ToString()))}");

        _latestVersion = version;
        _memory.Publish(snapshot, version);

        foreach (var height in await context.CarrierHeights.ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            _memory.RestoreCarrierHeight(new PanelId(height.PanelId), height.HeightMm, height.SetAt);
        }
    }
}
