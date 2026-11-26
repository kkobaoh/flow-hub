using FlowHub.Identity.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Identity.Tests;

public class RoleDirectoryTests
{
    [Fact]
    public async Task デフォルトの切り替えは既存ユーザーを変えず新規ユーザーだけに反映する()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        await RoleDirectory.InitializeAsync(db, token);
        var first = Assert.IsType<FirstAdministratorResult.Created>(await new FirstAdministratorStore(options).CreateAsync("first", token));
        var users = new UserLoginStore(options);
        var now = DateTimeOffset.UnixEpoch;
        var initial = Assert.IsType<UserLoginResult.Accepted>(await users.LoginAsync("first", first.TemporaryPassword, now, 5, TimeSpan.FromMinutes(15), token));
        var admin = Assert.IsType<PasswordChangeResult.Changed>(await users.ChangeInitialPasswordAsync(initial.Session, "permanent", 8, token));
        Assert.IsType<AddUserResult.Created>(await users.AddAsync(admin.Session, "before", token));
        Assert.Equal(DefaultRoleResult.Administrator, await RoleDirectory.SetDefaultAsync(db, "admin", token));
        Assert.Equal(DefaultRoleResult.NotFound, await RoleDirectory.SetDefaultAsync(db, "unknown", token));
        Assert.Equal("viewer", Assert.Single(await new RoleDirectory(options).ListAsync(token), r => r.IsDefault).Id);
        Assert.Equal(DefaultRoleResult.Changed, await RoleDirectory.SetDefaultAsync(db, "operator", token));
        Assert.IsType<AddUserResult.Created>(await users.AddAsync(admin.Session, "after", token));
        var roles = await new RoleDirectory(options).ListAsync(token);
        Assert.Equal("operator", Assert.Single(roles, r => r.IsDefault).Id);
        var directory = await users.ListAsync(now, token);
        Assert.Equal("viewer", Assert.Single(directory, u => u.Id == "before").Role);
        Assert.Equal("operator", Assert.Single(directory, u => u.Id == "after").Role);
    }

    [Fact]
    public async Task 既存DBに初期ロールを用意し再起動時に保存済みの設定を上書きしない()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var legacy = connection.CreateCommand();
        legacy.CommandText = "CREATE TABLE setup_completion (Id INTEGER PRIMARY KEY)";
        legacy.ExecuteNonQuery();
        void Configure(DbContextOptionsBuilder builder) => builder.UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning));
        Task Open() => IdentityDatabase.OpenAsync(Configure, (db, ct) => db.Database.EnsureCreatedAsync(ct), token);
        await Open();
        var roles = await IdentityDatabase.OpenRoles(Configure).ListAsync(token);
        Assert.Equal(4, roles.Count);
        var viewer = Assert.Single(roles, r => r.IsDefault);
        Assert.Equal("閲覧のみ", viewer.Name);
        Assert.Equal(TimeSpan.FromDays(7), viewer.IdleTimeout);
        Assert.Empty(viewer.Permissions);
        var admin = Assert.Single(roles, r => r.Fixed);
        Assert.Equal("admin", admin.Id);
        Assert.Contains("userAdmin", admin.Permissions);
        Assert.All(roles.Where(r => !r.Fixed), r => Assert.DoesNotContain("userAdmin", r.Permissions));
        legacy.CommandText = "UPDATE roles SET Name = '夜間運用' WHERE Id = 'operator'";
        legacy.ExecuteNonQuery();
        await Open();
        Assert.Contains(await IdentityDatabase.OpenRoles(Configure).ListAsync(token), r => r.Name == "夜間運用");
    }
}
