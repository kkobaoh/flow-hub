namespace FlowHub.Identity.Domain;

internal enum RoleKind
{
    Editable,
    Administrator,
}

internal static class RoleRules
{
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
