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

    /// <summary>マップを公開して、今の版にする。</summary>
    void Publish(MapSnapshot snapshot);

    /// <summary>受け渡しパネルのキャリアを下げる高さ（mm）。設定されていなければ null。</summary>
    int? CarrierHeightOf(PanelId panel);

    /// <summary>
    /// 受け渡しパネルのキャリアを下げる高さを設定する（現場での手入力、または AGV の自動測定）。新しく設定されたほうを使う。
    /// </summary>
    void SetCarrierHeight(PanelId panel, int heightMm, DateTimeOffset setAt);
}

/// <summary>
/// マップの保存場所（メモリーの中）。DB を使うまでの実装。
/// </summary>
public sealed class InMemoryMapStore : IMapStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<PanelId, (int HeightMm, DateTimeOffset SetAt)> _heights = [];
    private MapSnapshot? _current;

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

    public void Publish(MapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            _current = snapshot;
        }
    }

    public int? CarrierHeightOf(PanelId panel)
    {
        lock (_lock)
        {
            return _heights.TryGetValue(panel, out var height) ? height.HeightMm : null;
        }
    }

    public void SetCarrierHeight(PanelId panel, int heightMm, DateTimeOffset setAt)
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
                return; // すでに新しい設定がある
            }

            _heights[panel] = (heightMm, setAt);
        }
    }
}
