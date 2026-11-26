using FlowHub.Identity.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Identity.Tests;

public class UserLoginTests
{
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
