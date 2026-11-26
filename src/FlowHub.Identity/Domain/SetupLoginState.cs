namespace FlowHub.Identity.Domain;

internal sealed record SetupLoginState(int Failures, long LockedUntilTicks)
{
    internal (SetupLoginResult Result, SetupLoginState Next) Check(bool matches, DateTimeOffset now, int limit, TimeSpan lockDuration)
    {
        if (now.UtcTicks < LockedUntilTicks)
        {
            return (SetupLoginResult.Locked, this);
        }

        if (matches)
        {
            return (SetupLoginResult.Allowed, new(0, 0));
        }

        var failures = Failures + 1;
        return failures >= limit
            ? (SetupLoginResult.Locked, new(0, now.Add(lockDuration).UtcTicks))
            : (SetupLoginResult.Denied, new(failures, 0));
    }
}
