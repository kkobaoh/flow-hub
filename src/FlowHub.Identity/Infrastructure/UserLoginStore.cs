using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using FlowHub.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Identity.Infrastructure;

public sealed record UserSession(string UserId, string CredentialStamp, bool MustChangePassword);

public abstract record PasswordChangeResult
{
    private PasswordChangeResult() { }
    public sealed record TooShort : PasswordChangeResult;
    public sealed record Unchanged : PasswordChangeResult;
    public sealed record Expired : PasswordChangeResult;
    public sealed record Changed(UserSession Session) : PasswordChangeResult;
}

public abstract record UserLoginResult
{
    private UserLoginResult() { }
    public sealed record Denied : UserLoginResult;
    public sealed record Locked : UserLoginResult;
    public sealed record Accepted(UserSession Session) : UserLoginResult;
}

public sealed class UserLoginStore
{
    private readonly DbContextOptions<IdentityDbContext> _options;
    private readonly PasswordHasher<UserRow> _hasher = new();

    internal UserLoginStore(DbContextOptions<IdentityDbContext> options) => _options = options;

    public async Task<UserLoginResult> LoginAsync(string userId, string password, DateTimeOffset now, int limit,
        TimeSpan lockDuration, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockDuration, TimeSpan.Zero);
        UserId id;
        try { id = new UserId(userId); }
        catch (ArgumentException) { return new UserLoginResult.Denied(); }

        while (true)
        {
            await using var db = new IdentityDbContext(_options);
            var user = await db.Users.FindAsync([id.Value], token).ConfigureAwait(false);
            if (user is null || password is null) return new UserLoginResult.Denied();
            if (now.UtcTicks < user.LockedUntilTicks) return new UserLoginResult.Locked();
            var matches = _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
            var state = new SetupLoginState(user.Failures, user.LockedUntilTicks);
            var (result, next) = state.Check(matches, now, limit, lockDuration);
            if (state != next)
            {
                user.Failures = next.Failures;
                user.LockedUntilTicks = next.LockedUntilTicks;
                user.Version++;
                try { await db.SaveChangesAsync(token).ConfigureAwait(false); }
                catch (DbUpdateConcurrencyException) { continue; }
            }

            return result switch
            {
                SetupLoginResult.Allowed => new UserLoginResult.Accepted(Session(user)),
                SetupLoginResult.Locked => new UserLoginResult.Locked(),
                SetupLoginResult.Denied => new UserLoginResult.Denied(),
                _ => throw new InvalidOperationException("未対応のログイン結果です。"),
            };
        }
    }

    public async Task<bool> IsCurrentAsync(UserSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var db = new IdentityDbContext(_options);
        var user = await db.Users.FindAsync([session.UserId], token).ConfigureAwait(false);
        return user is not null && Session(user) == session;
    }

    public async Task<PasswordChangeResult> ChangeInitialPasswordAsync(UserSession session, string password, int minimumLength, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumLength);
        if (new StringInfo(password).LengthInTextElements < minimumLength) return new PasswordChangeResult.TooShort();
        await using var db = new IdentityDbContext(_options);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == session.UserId, token).ConfigureAwait(false);
        if (user is null || !user.MustChangePassword || Session(user) != session) return new PasswordChangeResult.Expired();
        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed)
            return new PasswordChangeResult.Unchanged();

        var hash = _hasher.HashPassword(user, password);
        var updated = await db.Users.Where(row => row.Id == user.Id && row.MustChangePassword && row.PasswordHash == user.PasswordHash)
            .ExecuteUpdateAsync(change => change.SetProperty(row => row.PasswordHash, hash)
                .SetProperty(row => row.MustChangePassword, false)
                .SetProperty(row => row.Version, row => row.Version + 1), token).ConfigureAwait(false);
        if (updated == 0) return new PasswordChangeResult.Expired();
        user.PasswordHash = hash;
        user.MustChangePassword = false;
        return new PasswordChangeResult.Changed(Session(user));
    }

    private static UserSession Session(UserRow user) =>
        new(user.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash))), user.MustChangePassword);
}
