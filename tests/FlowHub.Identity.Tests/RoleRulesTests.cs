using FlowHub.Identity.Domain;

namespace FlowHub.Identity.Tests;

public class RoleRulesTests
{
    [Fact]
    public void 管理者だけが管理権限を持ちデフォルトから除かれる()
    {
        Assert.True(RoleRules.CanManageUsers(RoleKind.Administrator));
        Assert.False(RoleRules.CanManageUsers(RoleKind.Editable));
        Assert.False(RoleRules.CanBeDefault(RoleKind.Administrator));
        Assert.True(RoleRules.CanBeDefault(RoleKind.Editable));
    }

    [Fact]
    public void ほかに管理者がいるときだけ管理者を降格できる()
    {
        Assert.False(RoleRules.CanReassign(RoleKind.Administrator, RoleKind.Editable, hasOtherAdministrator: false));
        Assert.True(RoleRules.CanReassign(RoleKind.Administrator, RoleKind.Editable, hasOtherAdministrator: true));
        Assert.True(RoleRules.CanReassign(RoleKind.Editable, RoleKind.Administrator, hasOtherAdministrator: true));
    }
}
