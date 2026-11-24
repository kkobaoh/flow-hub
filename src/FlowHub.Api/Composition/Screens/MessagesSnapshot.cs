using FlowHub.Notifications;

namespace FlowHub.Api.Composition.Screens;

/// <summary>
/// 画面へ送るメッセージの様子（残しているもの全部、新しい順）。変わるたびに番号を進め、画面は古い番号の様子を捨てる。
/// </summary>
/// <param name="Version">様子の番号。1から始まり、変わるたびに1つ進む。</param>
internal sealed record MessagesSnapshot(long Version, IReadOnlyList<MessageSnapshot> Messages);

/// <summary>
/// 画面へ送る1つのメッセージ。画面は、どこまで見たかを <paramref name="Sequence"/> で覚える。
/// </summary>
internal sealed record MessageSnapshot(long Sequence, DateTimeOffset At, string Title, string Detail)
{
    public static MessageSnapshot From(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return new MessageSnapshot(notice.Sequence, notice.At, notice.Title, notice.Detail);
    }
}
