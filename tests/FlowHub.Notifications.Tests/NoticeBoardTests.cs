namespace FlowHub.Notifications.Tests;

public sealed class NoticeBoardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 知らせに番号を1から順に付け今の時刻を入れて新しい順に出す()
    {
        var clock = new SettableTimeProvider(T0);
        var board = new InMemoryNoticeBoard(clock, capacity: 10);

        var first = await board.PostAsync("搬送を中断しました", "搬送 #11", Token);
        clock.Now = T0.AddMinutes(5);
        await board.PostAsync("予約が取れずに止まっています", "AGV-1", Token);

        Assert.Equal(1, first.Sequence);
        Assert.Equal(T0, first.At);
        Assert.Equal([2L, 1L], board.Recent.Select(n => n.Sequence));
        Assert.Equal(T0.AddMinutes(5), board.Recent[0].At);
    }

    [Fact]
    public async Task 残すのは新しいほうから決まった件数まで()
    {
        var board = new InMemoryNoticeBoard(new SettableTimeProvider(T0), capacity: 2);

        for (var i = 1; i <= 3; i++)
        {
            await board.PostAsync($"知らせ{i}", string.Empty, Token);
        }

        Assert.Equal(["知らせ3", "知らせ2"], board.Recent.Select(n => n.Title));
    }

    [Fact]
    public void 件数は1以上()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryNoticeBoard(TimeProvider.System, capacity: 0));
    }

    [Fact]
    public void 知らせの番号は1以上で見出しは空にできない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Notice(0, T0, "見出し", string.Empty));
        Assert.Throws<ArgumentException>(() => new Notice(1, T0, " ", string.Empty));
    }
}

/// <summary>テストで時刻を決める。</summary>
internal sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
