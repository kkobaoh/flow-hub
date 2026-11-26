using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowHub.Identity.Infrastructure;

internal sealed class SetupCompletion
{
    public int Id { get; set; }
}

internal sealed class SetupLoginRow
{
    public int Id { get; set; }
    public int Failures { get; set; }
    public long LockedUntilTicks { get; set; }
    public long Version { get; set; }
}

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<SetupLoginRow> SetupLogin => Set<SetupLoginRow>();
    public DbSet<SetupCompletion> SetupCompletions => Set<SetupCompletion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(IdentityDatabase.Schema);
        var login = modelBuilder.Entity<SetupLoginRow>();
        login.ToTable("setup_login");
        login.HasKey(row => row.Id);
        login.Property(row => row.Id).ValueGeneratedNever();
        login.Property(row => row.Version).IsConcurrencyToken();
        login.HasData(new SetupLoginRow { Id = 1 });
        var users = modelBuilder.Entity<UserRow>();
        users.ToTable("users");
        users.HasKey(row => row.Id);
        users.Property(row => row.Version).IsConcurrencyToken();
        var completion = modelBuilder.Entity<SetupCompletion>();
        completion.ToTable("setup_completion");
        completion.HasKey(row => row.Id);
        completion.Property(row => row.Id).ValueGeneratedNever();
    }
}

internal sealed class IdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.UseNpgsql("Host=localhost;Database=flowhub",
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDatabase.Schema));
        return new IdentityDbContext(builder.Options);
    }
}

public static class IdentityDatabase
{
    public const string Schema = "identity";

    public static UserLoginStore OpenUserLogins(Action<DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        configure(builder);
        return new UserLoginStore(builder.Options);
    }

    public static async Task<FirstAdministratorStore> OpenAsync(Action<DbContextOptionsBuilder> configure,
        Func<DbContext, CancellationToken, Task> initialize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(initialize);
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        configure(builder);
        await using var context = new IdentityDbContext(builder.Options);
        await initialize(context, cancellationToken).ConfigureAwait(false);
        if (context.Database.IsSqlite())
        {
            // EnsureCreated は既存の開発用 DB にテーブルを追加しない。CN-17b/c の DB を更新する。
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS users (Id TEXT NOT NULL PRIMARY KEY, RoleId TEXT NOT NULL, PasswordHash TEXT NOT NULL, MustChangePassword INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS setup_login (Id INTEGER NOT NULL PRIMARY KEY, Failures INTEGER NOT NULL, LockedUntilTicks INTEGER NOT NULL, Version INTEGER NOT NULL);
                INSERT OR IGNORE INTO setup_login (Id, Failures, LockedUntilTicks, Version) VALUES (1, 0, 0, 0);
                """, cancellationToken).ConfigureAwait(false);
            await using var upgrade = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var previousSchema = !await context.Database.SqlQueryRaw<string>(
                """SELECT name AS Value FROM pragma_table_info('users')""")
                .AnyAsync(name => name == "Version", cancellationToken).ConfigureAwait(false);
            if (previousSchema)
            {
                await context.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE users ADD COLUMN Failures INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE users ADD COLUMN LockedUntilTicks INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE users ADD COLUMN Version INTEGER NOT NULL DEFAULT 0;
                    """, cancellationToken).ConfigureAwait(false);
            }
            await upgrade.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        return new FirstAdministratorStore(builder.Options);
    }
}
