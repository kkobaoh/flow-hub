using FlowHub.Identity.Domain;

namespace FlowHub.Identity.Tests;

public class InitialRoleTests
{
    [Fact]
    public void 初期ロールの権限と無操作時間は権限表に従う()
    {
        var operation = new[] { Permission.Operation, Permission.EmergencyStop, Permission.TransportCancel,
            Permission.ControlBasic, Permission.StockerSetting, Permission.ConsumableOps };
        Permission[] maintenance = [.. operation, Permission.ControlAdvanced, Permission.Recovery, Permission.Manual, Permission.StockEdit];

        Assert.Empty(InitialRoles.Viewer.Permissions);
        Assert.Equal(TimeSpan.FromDays(7), InitialRoles.Viewer.IdleTimeout);
        Assert.True(InitialRoles.Viewer.IsDefault);
        Assert.True(InitialRoles.Operator.Permissions.SetEquals(operation));
        Assert.True(InitialRoles.Maintainer.Permissions.SetEquals(maintenance));
        Assert.True(InitialRoles.Administrator.Permissions.SetEquals([.. maintenance, Permission.Configure, Permission.UserAdmin]));
        Assert.Equal(RoleKind.Administrator, InitialRoles.Administrator.Kind);
        Assert.All(new[] { InitialRoles.Operator, InitialRoles.Maintainer, InitialRoles.Administrator }, role =>
        {
            Assert.False(role.IsDefault);
            Assert.Equal(TimeSpan.FromHours(8), role.IdleTimeout);
        });
    }
}
