using FlowHub.Identity.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Identity.Tests;

public class UserLoginTests
{
    [Fact]
    public async Task 本人変更はログインとロックを共有し成功後は新しいセッションだけが有効になる()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        var store = new UserLoginStore(options);
        var now = DateTimeOffset.UnixEpoch;
        var duration = TimeSpan.FromMinutes(15);
        Task<UserLoginResult> Login(string password) => store.LoginAsync("Admin", password, now, 5, duration, token);
        var first = Assert.IsType<FirstAdministratorResult.Created>(await new FirstAdministratorStore(options).CreateAsync("Admin", token));
        var initial = Assert.IsType<UserLoginResult.Accepted>(await Login(first.TemporaryPassword));
        var admin = Assert.IsType<PasswordChangeResult.Changed>(await store.ChangeInitialPasswordAsync(initial.Session, "password1", 8, token));
        Task<PasswordChangeResult> Change(string current, string next) =>
            store.ChangeOwnPasswordAsync(admin.Session, current, next, 8, now, 5, duration, token);
        Assert.IsType<PasswordChangeResult.Unchanged>(await Change("password1", "password1"));
        Assert.IsType<PasswordChangeResult.TooShort>(await Change("password1", "123456👩‍💻"));
        for (var i = 0; i < 3; i++) Assert.IsType<UserLoginResult.Denied>(await Login("wrong"));
        Assert.IsType<PasswordChangeResult.Incorrect>(await Change("wrong", "newpassword"));
        Assert.IsType<PasswordChangeResult.Locked>(await Change("wrong", "newpassword"));
        Assert.IsType<UserLoginResult.Locked>(await Login("password1"));
        Assert.IsType<PasswordChangeResult.Locked>(await Change("password1", "newpassword"));
        now += duration;
        const string chosen = " 123456👩‍💻";
        var changed = Assert.IsType<PasswordChangeResult.Changed>(await Change("password1", chosen));
        Assert.False(await store.IsCurrentAsync(admin.Session, token));
        Assert.True(await store.IsCurrentAsync(changed.Session, token));
        Assert.IsType<PasswordChangeResult.Expired>(await Change("password1", "another-password"));
        Assert.IsType<UserLoginResult.Denied>(await Login("password1"));
        Assert.IsType<UserLoginResult.Accepted>(await Login(chosen));
    }

    [Fact]
    public async Task 再発行は自分を対象にできず既存のセッションを失効させ失敗回数とロックを維持する()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        await RoleDirectory.InitializeAsync(db, token);
        var store = new UserLoginStore(options);
        var now = DateTimeOffset.UnixEpoch;
        Task<UserLoginResult> Login(string id, string password) => store.LoginAsync(id, password, now, 2, TimeSpan.FromMinutes(15), token);
        var first = Assert.IsType<FirstAdministratorResult.Created>(await new FirstAdministratorStore(options).CreateAsync("Admin", token));
        var temporary = Assert.IsType<UserLoginResult.Accepted>(await Login("Admin", first.TemporaryPassword));
        var admin = Assert.IsType<PasswordChangeResult.Changed>(await store.ChangeInitialPasswordAsync(temporary.Session, "permanent", 8, token));
        var added = Assert.IsType<AddUserResult.Created>(await store.AddAsync(admin.Session, "operator", token));
        var initial = Assert.IsType<UserLoginResult.Accepted>(await Login("operator", added.TemporaryPassword));
        var ready = Assert.IsType<PasswordChangeResult.Changed>(await store.ChangeInitialPasswordAsync(initial.Session, "old-password", 8, token));
        var other = Assert.IsType<UserLoginResult.Accepted>(await Login("operator", "old-password"));
        Assert.IsType<PasswordResetResult.Forbidden>(await store.ResetPasswordAsync(ready.Session, "Admin", token));
        Assert.IsType<PasswordResetResult.Forbidden>(await store.ResetPasswordAsync(admin.Session, "Admin", token));
        Assert.IsType<UserLoginResult.Denied>(await Login("operator", "wrong"));
        var reset = Assert.IsType<PasswordResetResult.Issued>(await store.ResetPasswordAsync(admin.Session, "operator", token));
        Assert.False(await store.IsCurrentAsync(ready.Session, token));
        Assert.False(await store.IsCurrentAsync(other.Session, token));
        Assert.IsType<UserLoginResult.Locked>(await Login("operator", "old-password"));
        var locked = Assert.Single(await store.ListAsync(now, token), u => u.Id == "operator");
        var again = Assert.IsType<PasswordResetResult.Issued>(await store.ResetPasswordAsync(admin.Session, "operator", token));
        Assert.IsType<UserLoginResult.Locked>(await Login("operator", again.TemporaryPassword));
        Assert.Equal(UnlockResult.Unlocked, await store.UnlockAsync(admin.Session, "operator", locked.LockToken!, now, token));
        Assert.IsType<UserLoginResult.Denied>(await Login("operator", reset.TemporaryPassword));
        Assert.True(Assert.IsType<UserLoginResult.Accepted>(await Login("operator", again.TemporaryPassword)).Session.MustChangePassword);
    }

    [Fact]
    public async Task 古い解除要求は同じ期限でも新しいロックを解除しない()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        await RoleDirectory.InitializeAsync(db, token);
        var first = Assert.IsType<FirstAdministratorResult.Created>(await new FirstAdministratorStore(options).CreateAsync("Admin", token));
        var store = new UserLoginStore(options);
        var now = DateTimeOffset.UnixEpoch;
        Task<UserLoginResult> Login(string id, string password) => store.LoginAsync(id, password, now, 1, TimeSpan.FromMinutes(15), token);
        var login = Assert.IsType<UserLoginResult.Accepted>(await Login("Admin", first.TemporaryPassword));
        var admin = Assert.IsType<PasswordChangeResult.Changed>(await store.ChangeInitialPasswordAsync(login.Session, "permanent", 8, token));
        var added = Assert.IsType<AddUserResult.Created>(await store.AddAsync(admin.Session, "viewer", token));
        async Task<string> Lock()
        {
            Assert.IsType<UserLoginResult.Locked>(await Login("viewer", "wrong"));
            return (await store.ListAsync(now, token)).Single(user => user.Id == "viewer").LockToken!;
        }
        Task<UnlockResult> Unlock(string seen) => store.UnlockAsync(admin.Session, "viewer", seen, now, token);
        var old = await Lock();
        Assert.Equal(UnlockResult.Unlocked, await Unlock(old));
        Assert.Equal(UnlockResult.Unlocked, await Unlock(old));
        var current = await Lock();
        Assert.NotEqual(old, current);
        Assert.Equal(UnlockResult.LockChanged, await Unlock(old));
        Assert.IsType<UserLoginResult.Locked>(await Login("viewer", added.TemporaryPassword));
        Assert.Equal(UnlockResult.Unlocked, await Unlock(current));
        Assert.True(Assert.IsType<UserLoginResult.Accepted>(await Login("viewer", added.TemporaryPassword)).Session.MustChangePassword);
        var expired = await Lock();
        now = now.AddMinutes(15);
        Assert.Equal(UnlockResult.Unlocked, await Unlock(expired));
        Assert.Null((await store.ListAsync(now, token)).Single(user => user.Id == "viewer").LockedUntil);
    }

    [Fact]
    public async Task 管理者の失敗回数を保存し期限後は仮ログインできる()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        var created = Assert.IsType<FirstAdministratorResult.Created>(await new FirstAdministratorStore(options).CreateAsync("Admin", token));
        var logins = new UserLoginStore(options);
        var time = DateTimeOffset.UnixEpoch;
        async Task<UserLoginResult> Login(string id, string password) =>
            await logins.LoginAsync(id, password, time, 2, TimeSpan.FromMinutes(15), token);

        Assert.IsType<UserLoginResult.Denied>(await Login("admin", created.TemporaryPassword));
        Assert.IsType<UserLoginResult.Denied>(await Login("Admin", "wrong"));
        Assert.IsType<UserLoginResult.Locked>(await Login("Admin", "wrong"));
        logins = new UserLoginStore(options);
        Assert.IsType<UserLoginResult.Locked>(await Login("Admin", created.TemporaryPassword));
        var locked = Assert.Single(await logins.ListAsync(time, token));
        Assert.Null(locked.LastLogin);
        Assert.Equal(time.AddMinutes(15), locked.LockedUntil);
        Assert.True(locked.MustChangePassword);
        time = time.AddMinutes(15);
        Assert.Null(Assert.Single(await logins.ListAsync(time, token)).LockedUntil);
        var login = Assert.IsType<UserLoginResult.Accepted>(await Login(" Admin ", created.TemporaryPassword));
        Assert.True(login.Session.MustChangePassword);
        Assert.Equal(time, Assert.Single(await logins.ListAsync(time, token)).LastLogin);
        Assert.Equal("Admin", login.Session.UserId);
        Assert.IsType<PasswordChangeResult.TooShort>(await logins.ChangeInitialPasswordAsync(login.Session, "123456👩‍💻", 8, token));
        Assert.IsType<PasswordChangeResult.Unchanged>(await logins.ChangeInitialPasswordAsync(login.Session, created.TemporaryPassword, 8, token));
        const string chosen = " a12345👩‍💻";
        var changed = Assert.IsType<PasswordChangeResult.Changed>(await logins.ChangeInitialPasswordAsync(login.Session, chosen, 8, token));
        Assert.False(changed.Session.MustChangePassword);
        Assert.False(await logins.IsCurrentAsync(login.Session, token));
        Assert.True(await logins.IsCurrentAsync(changed.Session, token));
        Assert.IsType<PasswordChangeResult.Expired>(await logins.ChangeInitialPasswordAsync(login.Session, "another password", 8, token));
        Assert.IsType<UserLoginResult.Denied>(await Login("Admin", created.TemporaryPassword));
        time = time.AddMinutes(10);
        Assert.IsType<UserLoginResult.Accepted>(await Login("Admin", chosen));
        var lastSuccess = time;
        time = time.AddMinutes(1);
        Assert.IsType<UserLoginResult.Denied>(await Login("Admin", chosen.Trim()));
        Assert.Equal(lastSuccess, Assert.Single(await logins.ListAsync(time, token)).LastLogin);
    }
}
