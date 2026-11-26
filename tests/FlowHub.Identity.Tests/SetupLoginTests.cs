using FlowHub.Identity.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowHub.Identity.Tests;

public class SetupLoginTests
{
    [Fact]
    public async Task 五回目でロックし別のインスタンスでも期限までは正しいパスワードを断る()
    {
        var token = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(SqliteEventId.SchemaConfiguredWarning)).Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.EnsureCreatedAsync(token);
        var now = DateTimeOffset.UnixEpoch;
        var store = new FirstAdministratorStore(options);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(SetupLoginResult.Denied, await Attempt(false, now));
        }

        Assert.Equal(SetupLoginResult.Locked, await Attempt(false, now));
        store = new FirstAdministratorStore(options);
        Assert.Equal(SetupLoginResult.Locked, await Attempt(true, now.AddMinutes(15).AddTicks(-1)));
        Assert.Equal(SetupLoginResult.Allowed, await Attempt(true, now.AddMinutes(15)));
        await store.CreateAsync("admin", token);
        Assert.Equal(SetupLoginResult.Completed, await Attempt(true, now.AddMinutes(16)));

        Task<SetupLoginResult> Attempt(bool matches, DateTimeOffset at) =>
            store.CheckLoginAsync(matches, at, 5, TimeSpan.FromMinutes(15), token);
    }
}
