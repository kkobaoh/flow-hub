namespace FlowHub.Map;

/// <summary>
/// マップの保存場所。今の版のスナップショットと、受け渡しパネルごとのキャリアの高さを持つ。
/// </summary>
/// <remarks>
/// キャリアの高さは、たびたび変わり、経路探索や予約に関係しないので、スナップショット（版）とは別に持つ。
/// </remarks>
public interface IMapStore
{
    /// <summary>今の版のマップ。まだ公開していなければ null。</summary>
    MapSnapshot? Current { get; }

    /// <summary>今の版の番号（公開した順に 1 から）。まだ公開していなければ 0。</summary>
    int CurrentVersion { get; }

    /// <summary>マップを公開して、今の版にする。</summary>
    void Publish(MapSnapshot snapshot);

    /// <summary>受け渡しパネルのキャリアを下げる高さ（mm）。設定されていなければ null。</summary>
    int? CarrierHeightOf(PanelId panel);

    /// <summary>
    /// 受け渡しパネルのキャリアを下げる高さを設定する（現場での手入力、または AGV の自動測定）。新しく設定されたほうを使う。
    /// </summary>
    void SetCarrierHeight(PanelId panel, int heightMm, DateTimeOffset setAt);

    /// <summary>受け渡しパネルのキャリアの高さの設定（高さ、出どころ、日時）。設定されていなければ null。</summary>
    CarrierHeightSetting? CarrierHeightSettingOf(PanelId panel);

    /// <summary>
    /// 人が画面から入れたキャリアの高さを設定する。すぐ使う。null なら設定なしにする（AGV が測る）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">高さが 1 未満のとき（頼みの誤り）。</exception>
    CarrierHeightSaveResult SetManualCarrierHeight(PanelId panel, int? heightMm, DateTimeOffset setAt);
}

/// <summary>
/// マップの保存場所（メモリーの中）。テストで使い、DB に保存する置き場（<see cref="Infrastructure.DbMapStore"/>）の手元の写しにも使う。
/// </summary>
public sealed class InMemoryMapStore : IMapStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<PanelId, CarrierHeightSetting> _heights = [];
    private MapSnapshot? _current;
    private int _version;

    public MapSnapshot? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public int CurrentVersion
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    public void Publish(MapSnapshot snapshot) => Publish(snapshot, version: null);

    /// <summary>マップを今の版にする。番号を指定しなければ、次の番号にする（DB から読んだときは、DB の番号を使う）。</summary>
    internal void Publish(MapSnapshot snapshot, int? version)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            _current = snapshot;
            _version = version ?? _version + 1;
        }
    }

    public int? CarrierHeightOf(PanelId panel)
    {
        lock (_lock)
        {
            return _heights.TryGetValue(panel, out var height) ? height.HeightMm : null;
        }
    }

    public void SetCarrierHeight(PanelId panel, int heightMm, DateTimeOffset setAt) => TrySetCarrierHeight(panel, heightMm, setAt);

    public CarrierHeightSetting? CarrierHeightSettingOf(PanelId panel)
    {
        lock (_lock)
        {
            return _heights.GetValueOrDefault(panel);
        }
    }

    public CarrierHeightSaveResult SetManualCarrierHeight(PanelId panel, int? heightMm, DateTimeOffset setAt)
    {
        if (heightMm is { } mm)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mm, nameof(heightMm));
        }

        lock (_lock)
        {
            if (_current is not { } map)
            {
                return CarrierHeightSaveResult.NoMap;
            }

            if (!map.Panels.Any(p => p.Id == panel && p.Type == PanelType.Transfer))
            {
                return CarrierHeightSaveResult.NotTransferPanel;
            }

            if (heightMm is { } height)
            {
                _heights[panel] = new CarrierHeightSetting(height, CarrierHeightSource.Manual, setAt);
            }
            else
            {
                _heights.Remove(panel);
            }

            return CarrierHeightSaveResult.Saved;
        }
    }

    /// <summary>
    /// AGV が測ったキャリアの高さを設定する。すでに新しい設定があって、設定しなかったときは false。
    /// </summary>
    internal bool TrySetCarrierHeight(PanelId panel, int heightMm, DateTimeOffset setAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightMm);
        lock (_lock)
        {
            var map = _current ?? throw new InvalidOperationException("マップを公開する前は、キャリアの高さを設定できません。");
            if (!map.Panels.Any(p => p.Id == panel && p.Type == PanelType.Transfer))
            {
                throw new ArgumentException($"{panel} は受け渡しパネルではありません。", nameof(panel));
            }

            if (_heights.TryGetValue(panel, out var existing) && existing.SetAt > setAt)
            {
                return false; // すでに新しい設定がある
            }

            _heights[panel] = new CarrierHeightSetting(heightMm, CarrierHeightSource.Measured, setAt);
            return true;
        }
    }

    /// <summary>
    /// 保存しておいたキャリアの高さを戻す（起動したとき）。保存したときに確かめているので、もう一度は確かめない。
    /// </summary>
    internal void RestoreCarrierHeight(PanelId panel, CarrierHeightSetting setting)
    {
        lock (_lock)
        {
            _heights[panel] = setting;
        }
    }
}
