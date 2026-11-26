using FlowHub.Identity.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Identity.Tests;

public class RoleDirectoryTests
{
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
