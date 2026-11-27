namespace FlowHub.Identity.Domain;

internal enum RoleKind
{
    Editable,
    Administrator,
}

internal static class RoleRules
{
    internal static bool TryGetIdleTimeout(string? minutes, DateTimeOffset now, out TimeSpan timeout)
    {
        timeout = default;
        if (!long.TryParse(minutes, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var duration)
            || duration < 1 || duration > (DateTimeOffset.MaxValue - now).Ticks / TimeSpan.TicksPerMinute)
            return false;
        timeout = TimeSpan.FromTicks(duration * TimeSpan.TicksPerMinute);
        return true;
    }

    internal static bool CanManageUsers(RoleKind role) => role switch
    {
        RoleKind.Administrator => true,
        RoleKind.Editable => false,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    internal static bool CanBeDefault(RoleKind role) => !CanManageUsers(role);

    internal static bool CanReassign(RoleKind current, RoleKind target, bool hasOtherAdministrator)
    {
        var isAdministrator = CanManageUsers(current);
        var staysOrBecomesAdministrator = CanManageUsers(target);
        return !isAdministrator || staysOrBecomesAdministrator || hasOtherAdministrator;
    }
}
