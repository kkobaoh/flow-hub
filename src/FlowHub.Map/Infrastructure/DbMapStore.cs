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

    internal DbMapStore(Func<MapDbContext> createContext) => _createContext = createContext;

    public MapSnapshot? Current => _memory.Current;

    public int? CarrierHeightOf(PanelId panel) => _memory.CarrierHeightOf(panel);

    /// <summary>
    /// マップを公開する。次の番号の版として DB に保存してから、今の版にする。
    /// </summary>
    public void Publish(MapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            var version = _latestVersion + 1;
            using (var context = _createContext())
            {
                context.Versions.Add(new MapVersionRow { Version = version });
                context.Panels.AddRange(snapshot.Panels.Select((panel, order) => new PanelRow
                {
                    Version = version,
                    Order = order,
                    PanelId = panel.Id.Value,
                    Type = panel.Type.ToString(),
                    HasChargingPin = panel.HasChargingPin,
                    CarrierLoweringHeading = panel.CarrierLoweringHeading?.Degrees,
                }));
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
            _memory.Publish(snapshot);
        }
    }

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
        var latest = await context.Versions.Select(v => (int?)v.Version).MaxAsync(cancellationToken).ConfigureAwait(false);
        if (latest is not { } version)
        {
            return;
        }

        var panels = await context.Panels.Where(p => p.Version == version).OrderBy(p => p.Order).ToListAsync(cancellationToken).ConfigureAwait(false);
        var connections = await context.Connections.Where(c => c.Version == version).OrderBy(c => c.Order).ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = MapSnapshot.Create(
            panels.Select(p => new Panel(new PanelId(p.PanelId), Enum.Parse<PanelType>(p.Type), p.HasChargingPin)
            {
                CarrierLoweringHeading = p.CarrierLoweringHeading is { } heading ? new Heading(heading) : null,
            }),
            connections.Select(c => new Connection(new PanelId(c.FromPanelId), new PanelId(c.ToPanelId), c.DistanceMm, new Heading(c.HeadingDegrees), c.OneWay)));
        var snapshot = result.Snapshot
            ?? throw new InvalidOperationException($"保存してあるマップの版 {version} が、検証を通りません：{string.Join("、", result.Errors.Select(e => e.ToString()))}");

        _latestVersion = version;
        _memory.Publish(snapshot);

        foreach (var height in await context.CarrierHeights.ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            _memory.RestoreCarrierHeight(new PanelId(height.PanelId), height.HeightMm, height.SetAt);
        }
    }
}
