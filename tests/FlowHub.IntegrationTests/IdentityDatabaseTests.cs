using FlowHub.Api.Database;
using FlowHub.Identity.Infrastructure;

namespace FlowHub.IntegrationTests;

public class IdentityDatabaseTests
{
    [Fact]
    public async Task PostgreSQLで初期設定からパスワードの変更まで保存できる()
    {
        var token = TestContext.Current.CancellationToken;
        var database = LocalPostgres.Options();
        await LocalPostgres.DropSchemaAsync(database, IdentityDatabase.Schema, token);
        var setup = await IdentityDatabase.OpenAsync(b => Databases.Configure(b, database, IdentityDatabase.Schema),
            (context, ct) => Databases.InitializeAsync(context, database, ct), token);
        var created = Assert.IsType<FirstAdministratorResult.Created>(await setup.CreateAsync("first", token));
        var roles = await IdentityDatabase.OpenRoles(b => Databases.Configure(b, database, IdentityDatabase.Schema)).ListAsync(token);
        Assert.Equal("admin", Assert.Single(roles, role => role.Fixed).Id);
        Assert.Equal("viewer", Assert.Single(roles, role => role.IsDefault).Id);
        Assert.True(await setup.IsSetupCompleteAsync(token));
        var logins = IdentityDatabase.OpenUserLogins(b => Databases.Configure(b, database, IdentityDatabase.Schema));
        var login = Assert.IsType<UserLoginResult.Accepted>(await logins.LoginAsync("first", created.TemporaryPassword,
            DateTimeOffset.UnixEpoch, 5, TimeSpan.FromMinutes(15), token));
        var changed = Assert.IsType<PasswordChangeResult.Changed>(await logins.ChangeInitialPasswordAsync(login.Session, "local-test-password", 8, token));
        Assert.True(await logins.IsCurrentAsync(changed.Session, token));
        Assert.False(await logins.IsCurrentAsync(login.Session, token));
        var now = DateTimeOffset.UnixEpoch;
        Assert.IsType<AddUserResult.Created>(await logins.AddAsync(changed.Session, "new-user", token));
        Assert.IsType<UserLoginResult.Locked>(await logins.LoginAsync("new-user", "wrong", now, 1, TimeSpan.FromMinutes(15), token));
        var locked = Assert.Single(await logins.ListAsync(now, token), user => user.Id == "new-user");
        Assert.NotNull(locked.LockToken);
        Assert.Equal(UnlockResult.Unlocked, await logins.UnlockAsync(changed.Session, locked.Id, locked.LockToken, now, token));
        Assert.Null(Assert.Single(await logins.ListAsync(now, token), user => user.Id == locked.Id).LockedUntil);
    }
}
