namespace FlowHub.Notifications;

/// <summary>
/// 画面で知らせるメッセージの置き場。新しいほうから決まった件数だけ残す。
/// </summary>
/// <remarks>
/// どのコンテキストにも依存しない。何を知らせるか（イベントを文にすること）は、組み立て（FlowHub.Api）の購読者が決める。
/// </remarks>
public interface INoticeBoard
{
    /// <summary>残している知らせ（新しい順。最大で、決まった件数）。</summary>
    IReadOnlyList<Notice> Recent { get; }

    /// <summary>知らせを残す。次の番号と今の時刻を付ける。</summary>
    Task<Notice> PostAsync(string title, string detail, CancellationToken cancellationToken);
}

/// <summary>
/// 知らせの置き場（メモリーの中）。テストで使い、DB に残す置き場（<see cref="Infrastructure.DbNoticeBoard"/>）の手元の写しにも使う。
/// </summary>
public sealed class InMemoryNoticeBoard : INoticeBoard
{
    /// <summary>残す件数の既定値。</summary>
    public const int DefaultCapacity = 200;

    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly Lock _lock = new();
    private readonly List<Notice> _notices = []; // 新しい順
    private long _lastSequence;

    public InMemoryNoticeBoard(TimeProvider timeProvider, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _timeProvider = timeProvider;
        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public IReadOnlyList<Notice> Recent
    {
        get
        {
            lock (_lock)
            {
                return [.. _notices];
            }
        }
    }

    public Task<Notice> PostAsync(string title, string detail, CancellationToken cancellationToken)
    {
        var notice = Create(title, detail);
        Keep(notice);
        return Task.FromResult(notice);
    }

    /// <summary>次の番号と今の時刻を付けた知らせを作る（まだ残さない）。</summary>
    internal Notice Create(string title, string detail)
    {
        lock (_lock)
        {
            var notice = new Notice(_lastSequence + 1, _timeProvider.GetUtcNow(), title, detail);
            _lastSequence = notice.Sequence;
            return notice;
        }
    }

    /// <summary>知らせを残す。番号の順に並べ、決まった件数より古いものは捨てる。</summary>
    internal void Keep(Notice notice)
    {
        lock (_lock)
        {
            var index = _notices.FindIndex(n => n.Sequence < notice.Sequence);
            _notices.Insert(index < 0 ? _notices.Count : index, notice);
            if (_notices.Count > _capacity)
            {
                _notices.RemoveRange(_capacity, _notices.Count - _capacity);
            }

            _lastSequence = Math.Max(_lastSequence, notice.Sequence);
        }
    }
}
