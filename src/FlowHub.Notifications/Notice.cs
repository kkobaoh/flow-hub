namespace FlowHub.Notifications;

/// <summary>
/// 画面で知らせるメッセージ（docs/screens.md「メッセージの列」）。番号は、知らせた順に 1 から付ける。
/// </summary>
public sealed record Notice
{
    public Notice(long sequence, DateTimeOffset at, string title, string detail)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(detail);
        Sequence = sequence;
        At = at;
        Title = title;
        Detail = detail;
    }

    /// <summary>知らせた順の番号（1 から）。画面は、どこまで見たかをこの番号で覚える。</summary>
    public long Sequence { get; }

    /// <summary>知らせた時刻。</summary>
    public DateTimeOffset At { get; }

    /// <summary>何が起きたか（短い見出し）。</summary>
    public string Title { get; }

    /// <summary>くわしい中身（どの AGV、どの搬送など）。</summary>
    public string Detail { get; }
}
