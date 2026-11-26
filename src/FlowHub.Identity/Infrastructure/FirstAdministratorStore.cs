using System.Security.Cryptography;
using FlowHub.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Identity.Infrastructure;

internal sealed class UserRow
{
    public required string Id { get; init; }
    public required string RoleId { get; init; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public long? LastLoginTicks { get; set; }
    public int Failures { get; set; }
    public long LockedUntilTicks { get; set; }
    public long LockGeneration { get; set; }
    public long Version { get; set; }

    public static UserRow Create(UserId id, string roleId, out string temporaryPassword)
    {
        temporaryPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var user = new UserRow { Id = id.Value, RoleId = roleId, MustChangePassword = true };
        user.PasswordHash = new PasswordHasher<UserRow>().HashPassword(user, temporaryPassword);
        return user;
    }
}

public abstract class FirstAdministratorResult
{
    private FirstAdministratorResult() { }

    public sealed class Created(string temporaryPassword) : FirstAdministratorResult
    {
        public string TemporaryPassword { get; } = temporaryPassword;
    }

    public sealed class AlreadyCompleted : FirstAdministratorResult;
}

public sealed class FirstAdministratorStore
{
    private readonly DbContextOptions<IdentityDbContext> _options;

    internal FirstAdministratorStore(DbContextOptions<IdentityDbContext> options) => _options = options;

    public async Task<SetupLoginResult> CheckLoginAsync(bool matches, DateTimeOffset now, int failureLimit,
        TimeSpan lockDuration, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockDuration, TimeSpan.Zero);
        while (true)
        {
            await using var db = new IdentityDbContext(_options);
            if (await db.SetupCompletions.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return SetupLoginResult.Completed;
            }

            var row = await db.SetupLogin.SingleAsync(cancellationToken).ConfigureAwait(false);
            var state = new SetupLoginState(row.Failures, row.LockedUntilTicks);
            var (result, next) = state.Check(matches, now, failureLimit, lockDuration);
            if (next == state) return result;
            row.Failures = next.Failures;
            row.LockedUntilTicks = next.LockedUntilTicks;
            row.Version++;
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                // 同時に届いた試行を取りこぼさず、新しい失敗回数と期限で判断し直す。
            }
        }
    }

    public async Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken)
    {
        await using var db = new IdentityDbContext(_options);
        return await db.SetupCompletions.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirstAdministratorResult> CreateAsync(string userId, CancellationToken cancellationToken)
    {
        var id = new UserId(userId);
        await using var db = new IdentityDbContext(_options);
        if (await db.SetupCompletions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new FirstAdministratorResult.AlreadyCompleted();
        }

        db.Users.Add(UserRow.Create(id, InitialRoles.Administrator.Id, out var temporaryPassword));
        // 固定の主キーで競合を検出する。2行は SaveChanges の同じトランザクションで保存する。
        db.SetupCompletions.Add(new SetupCompletion { Id = 1 });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await db.SetupCompletions.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return new FirstAdministratorResult.AlreadyCompleted();
            }

            throw;
        }

        return new FirstAdministratorResult.Created(temporaryPassword);
    }
}
