namespace FlowHub.Traffic;

/// <summary>
/// AGV ごとの、コントロールステータスの保存場所。システムを再起動しても、ステータスを残すために使う。
/// </summary>
public interface IControlStatusStore
{
    /// <summary>保存しているコントロールステータス。</summary>
    ControlStatus StatusOf(AgvId id);

    /// <summary>コントロールステータスを保存する。</summary>
    void Save(AgvId id, ControlStatus status);
}

/// <summary>
/// コントロールステータスを、メモリーの中にだけ持つ（DB を使わないとき。テスト用で、オートから始める）。
/// </summary>
public sealed class InMemoryControlStatusStore : IControlStatusStore
{
    private readonly Dictionary<AgvId, ControlStatus> _statuses = [];
    private readonly Lock _lock = new();

    public ControlStatus StatusOf(AgvId id)
    {
        lock (_lock)
        {
            return _statuses.GetValueOrDefault(id, ControlStatus.Auto);
        }
    }

    public void Save(AgvId id, ControlStatus status)
    {
        lock (_lock)
        {
            _statuses[id] = status;
        }
    }
}
