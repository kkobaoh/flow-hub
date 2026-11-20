namespace FlowHub.Api.Tests;

/// <summary>時刻を手で進められ、タイマーも、進めた時刻に合わせて動く。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly Lock _lock = new();

    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return Now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>時刻を進め、期限が来たタイマーを呼ぶ。</summary>
    public void Advance(TimeSpan by)
    {
        ManualTimer[] timers;
        lock (_lock)
        {
            Now += by;
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.FireIfDue();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
            return true;
        }

        public void FireIfDue()
        {
            if (_due is { } due && clock.GetUtcNow() >= due)
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
