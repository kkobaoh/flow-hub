using System.Collections.Frozen;

namespace FlowHub.Identity.Domain;

internal enum Permission
{
    Operation,
    EmergencyStop,
    TransportCancel,
    ControlBasic,
    StockerSetting,
    ConsumableOps,
    ControlAdvanced,
    Recovery,
    Manual,
    StockEdit,
    Configure,
    UserAdmin,
}

internal static class InitialRoles
{
    internal sealed record Definition(string Id, string Name, RoleKind Kind, bool IsDefault, TimeSpan IdleTimeout, FrozenSet<Permission> Permissions);

    internal static Definition Viewer { get; } = new("viewer", "閲覧のみ", RoleKind.Editable, true,
        TimeSpan.FromDays(7), FrozenSet<Permission>.Empty);

    internal static Definition Operator { get; } = new("operator", "運用", RoleKind.Editable, false, TimeSpan.FromHours(8),
        Permissions(Permission.Operation, Permission.EmergencyStop, Permission.TransportCancel,
            Permission.ControlBasic, Permission.StockerSetting, Permission.ConsumableOps));

    internal static Definition Maintainer { get; } = new("maintainer", "保守", RoleKind.Editable, false, TimeSpan.FromHours(8),
        Permissions([.. Operator.Permissions, Permission.ControlAdvanced, Permission.Recovery,
            Permission.Manual, Permission.StockEdit]));

    internal static Definition Administrator { get; } = new("admin", "管理者", RoleKind.Administrator, false, TimeSpan.FromHours(8),
        Permissions([.. Maintainer.Permissions, Permission.Configure, Permission.UserAdmin]));

    private static FrozenSet<Permission> Permissions(params Permission[] permissions) => permissions.ToFrozenSet();
}
