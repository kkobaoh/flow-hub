using FlowHub.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Identity.Tests;

public class FirstAdministratorTests
{
    [Fact]
    public async Task 最初の管理者と完了記録を保存し再試行では仮パスワードを返さない()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);

        var created = Assert.IsType<FirstAdministratorResult.Created>(
            await new FirstAdministratorStore(options).CreateAsync("  Admin01　", token));
        Assert.IsType<FirstAdministratorResult.AlreadyCompleted>(
            await new FirstAdministratorStore(options).CreateAsync("another", token));
        var admin = await db.Users.SingleAsync(token);
        Assert.Equal("Admin01", admin.Id);
        Assert.Equal("admin", admin.RoleId);
        Assert.True(admin.MustChangePassword);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<UserRow>().VerifyHashedPassword(admin, admin.PasswordHash, created.TemporaryPassword));
        Assert.Equal(1, await db.SetupCompletions.CountAsync(token));
    }

    [Fact]
    public async Task 管理者の保存が失敗した場合は初期設定を完了しない()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_insert BEFORE INSERT ON users BEGIN SELECT RAISE(ABORT, 'test'); END", token);
        await Assert.ThrowsAsync<DbUpdateException>(() => new FirstAdministratorStore(options).CreateAsync("first", token));
        Assert.False(await db.SetupCompletions.AnyAsync(token));
        Assert.False(await db.Users.AnyAsync(token));
    }
}
