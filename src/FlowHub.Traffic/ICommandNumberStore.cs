namespace FlowHub.Traffic;

/// <summary>
/// AGV ごとの、次に付けるコマンドの番号の保存場所。システムを再起動しても、番号を 1 に戻さず続きから数えるために使う。
/// </summary>
public interface ICommandNumberStore
{
    /// <summary>次に付ける番号。まだ番号を付けたことがなければ 1。</summary>
    long NextNumberOf(AgvId id);

    /// <summary>次に付ける番号を保存する。</summary>
    void Save(AgvId id, long nextNumber);
}

/// <summary>
/// 次に付けるコマンドの番号を、メモリーの中にだけ持つ（DB を使わないとき。再起動すると 1 に戻る）。
/// </summary>
public sealed class InMemoryCommandNumberStore : ICommandNumberStore
{
    private readonly Dictionary<AgvId, long> _numbers = [];
    private readonly Lock _lock = new();

    public long NextNumberOf(AgvId id)
    {
        lock (_lock)
        {
            return _numbers.GetValueOrDefault(id, 1);
        }
    }

    public void Save(AgvId id, long nextNumber)
    {
        lock (_lock)
        {
            _numbers[id] = nextNumber;
        }
    }
}
