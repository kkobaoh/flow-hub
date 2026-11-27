using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using FlowHub.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Identity.Infrastructure;

public sealed record UserSession(string UserId, string CredentialStamp, bool MustChangePassword);
public sealed record CurrentAccount(string UserId, IReadOnlyList<string> Permissions);

public enum RoleChangeResult { Changed, Forbidden, NotFound, LastAdministrator }

public enum UnlockResult { Unlocked, Forbidden, NotFound, LockChanged }

public sealed record UserDetails(string Id, string Role, bool MustChangePassword, DateTimeOffset? LockedUntil, DateTimeOffset? LastLogin, string? LockToken);

public abstract class AddUserResult
{
    private AddUserResult() { }
    public sealed class Created(string temporaryPassword) : AddUserResult
    {
        public string TemporaryPassword { get; } = temporaryPassword;
    }
    public sealed class Forbidden : AddUserResult;
    public sealed class Duplicate : AddUserResult;
    public sealed class InvalidId : AddUserResult;
}

public abstract class PasswordResetResult
{
    private PasswordResetResult() { }
    public sealed class Issued(string temporaryPassword) : PasswordResetResult
    {
        public string TemporaryPassword { get; } = temporaryPassword;
    }
    public sealed class Forbidden : PasswordResetResult;
    public sealed class NotFound : PasswordResetResult;
}

public abstract record PasswordChangeResult
{
    private PasswordChangeResult() { }
    public sealed record TooShort : PasswordChangeResult;
    public sealed record Unchanged : PasswordChangeResult;
    public sealed record Incorrect : PasswordChangeResult;
    public sealed record Locked : PasswordChangeResult;
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

    public async Task<CurrentAccount?> GetCurrentAccountAsync(UserSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var db = new IdentityDbContext(_options);
        var account = await (from user in db.Users.AsNoTracking()
                             join role in db.Roles on user.RoleId equals role.Id
                             where user.Id == session.UserId && !user.MustChangePassword
                             select new { User = user, Permissions = role.Permissions.Select(p => p.Permission).ToList() })
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        return account is not null && Session(account.User) == session
            ? new CurrentAccount(session.UserId, account.Permissions) : null;
    }

    public async Task<TimeSpan> GetIdleTimeoutAsync(string userId, CancellationToken token)
    {
        await using var db = new IdentityDbContext(_options);
        var role = await (from user in db.Users
                          join r in db.Roles on user.RoleId equals r.Id
                          where user.Id == userId
                          select r.IdleTimeoutTicks).SingleAsync(token).ConfigureAwait(false);
        return TimeSpan.FromTicks(role);
    }

    public async Task<AddUserResult> AddAsync(UserSession actor, string userId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false))
            return new AddUserResult.Forbidden();

        UserId id;
        try { id = new UserId(userId); }
        catch (ArgumentException) { return new AddUserResult.InvalidId(); }
        if (await db.Users.AnyAsync(u => u.Id == id.Value, token).ConfigureAwait(false)) return new AddUserResult.Duplicate();
        var roleId = await db.Roles.Where(r => r.IsDefault && r.Id != InitialRoles.Administrator.Id)
            .Select(r => r.Id).SingleAsync(token).ConfigureAwait(false);
        db.Users.Add(UserRow.Create(id, roleId, out var password));
        try
        {
            await db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await db.Users.AnyAsync(u => u.Id == id.Value, token).ConfigureAwait(false)) return new AddUserResult.Duplicate();
            throw;
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new AddUserResult.Created(password);
    }

    private static async Task<bool> LockAndCheckAdministratorAsync(IdentityDbContext db, UserSession actor, CancellationToken token)
    {
        // 固定ロールの行を先にロックし、管理者の権限の確認から更新までを直列にする。
        await db.Roles.Where(r => r.Id == InitialRoles.Administrator.Id)
            .ExecuteUpdateAsync(change => change.SetProperty(r => r.Name, r => r.Name), token).ConfigureAwait(false);
        var caller = await db.Users.FindAsync([actor.UserId], token).ConfigureAwait(false);
        return !actor.MustChangePassword && caller is not null && Session(caller) == actor && caller.RoleId == InitialRoles.Administrator.Id;
    }

    public async Task<SaveRoleResult> SaveRoleAsync(UserSession actor, string? roleId, string? name, string? minutes, DateTimeOffset now, CancellationToken token)
    {
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false)) return SaveRoleResult.Forbidden;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return SaveRoleResult.InvalidName;
        if (!RoleRules.TryGetIdleTimeout(minutes, now, out var timeout))
            return SaveRoleResult.InvalidTimeout;
        var role = roleId is null ? new RoleRow { Id = Guid.NewGuid().ToString("N"), Name = name } :
            await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, token).ConfigureAwait(false);
        if (role is null) return SaveRoleResult.NotFound;
        if (role.Id == InitialRoles.Administrator.Id && role.Name != name) return SaveRoleResult.InvalidName;
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != role.Id, token).ConfigureAwait(false)) return SaveRoleResult.Duplicate;
        role.Name = name;
        role.IdleTimeoutTicks = timeout.Ticks;
        role.IdleTimeoutInitialized = true;
        if (roleId is null) db.Roles.Add(role);
        await db.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return SaveRoleResult.Saved;
    }

    public async Task<SavePermissionsResult> SavePermissionsAsync(UserSession actor, IReadOnlyList<PermissionChange> changes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(changes);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false)) return SavePermissionsResult.Forbidden;
        var cells = new HashSet<(string, string)>();
        foreach (var change in changes)
        {
            if (change is null || string.IsNullOrEmpty(change.RoleId) || change.RoleId == InitialRoles.Administrator.Id
                || !RoleDirectory.EditablePermissions.Contains(change.Permission) || !cells.Add((change.RoleId, change.Permission)))
                return SavePermissionsResult.Invalid;
        }
        var ids = changes.Select(change => change.RoleId).Distinct().ToArray();
        var roles = await db.Roles.Where(role => ids.Contains(role.Id)).Include(role => role.Permissions)
            .ToDictionaryAsync(role => role.Id, token).ConfigureAwait(false);
        if (roles.Count != ids.Length) return SavePermissionsResult.NotFound;
        foreach (var change in changes)
        {
            var permissions = roles[change.RoleId].Permissions;
            var row = permissions.Find(p => p.Permission == change.Permission);
            if (change.Enabled && row is null)
                permissions.Add(new RolePermissionRow { RoleId = change.RoleId, Permission = change.Permission });
            else if (!change.Enabled && row is not null)
                db.Remove(row);
        }
        await db.SaveChangesAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return SavePermissionsResult.Saved;
    }

    public async Task<DeleteRoleResult> DeleteRoleAsync(UserSession actor, string roleId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false)) return DeleteRoleResult.Forbidden;
        var target = db.Roles.Where(role => role.Id == roleId);
        var deleted = await target.Where(role => role.Id != InitialRoles.Administrator.Id && !role.IsDefault
            && !db.Users.Any(user => user.RoleId == role.Id)).ExecuteDeleteAsync(token).ConfigureAwait(false);
        if (deleted == 0)
            return await target.AnyAsync(token).ConfigureAwait(false) ? DeleteRoleResult.Protected : DeleteRoleResult.NotFound;
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return DeleteRoleResult.Deleted;
    }

    public async Task<DefaultRoleResult> SetDefaultRoleAsync(UserSession actor, string roleId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false)) return DefaultRoleResult.Forbidden;
        var result = await RoleDirectory.SetDefaultAsync(db, roleId, token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return result;
    }

    public async Task<RoleChangeResult> ChangeRoleAsync(UserSession actor, string userId, string roleId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false)) return RoleChangeResult.Forbidden;
        var user = await db.Users.FindAsync([userId], token).ConfigureAwait(false);
        if (user is null || !await db.Roles.AnyAsync(r => r.Id == roleId, token).ConfigureAwait(false)) return RoleChangeResult.NotFound;
        var adminId = InitialRoles.Administrator.Id;
        var other = await db.Users.AnyAsync(u => u.Id != userId && u.RoleId == adminId, token).ConfigureAwait(false);
        if (!RoleRules.CanReassign(user.RoleId == adminId ? RoleKind.Administrator : RoleKind.Editable,
            roleId == adminId ? RoleKind.Administrator : RoleKind.Editable, other)) return RoleChangeResult.LastAdministrator;
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(change => change
            .SetProperty(u => u.RoleId, roleId).SetProperty(u => u.Version, u => u.Version + 1), token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return RoleChangeResult.Changed;
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(UserSession actor, string userId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false) || actor.UserId == userId)
            return new PasswordResetResult.Forbidden();
        var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, token).ConfigureAwait(false);
        if (target is null) return new PasswordResetResult.NotFound();
        var password = target.IssueTemporaryPassword();
        await db.Users.Where(user => user.Id == userId).ExecuteUpdateAsync(change => change
            .SetProperty(user => user.PasswordHash, target.PasswordHash)
            .SetProperty(user => user.MustChangePassword, true)
            .SetProperty(user => user.Version, user => user.Version + 1), token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return new PasswordResetResult.Issued(password);
    }

    public async Task<IReadOnlyList<UserDetails>> ListAsync(DateTimeOffset now, CancellationToken token)
    {
        await using var db = new IdentityDbContext(_options);
        var rows = await db.Users.AsNoTracking().Select(user => new
        {
            user.Id, user.RoleId, user.MustChangePassword, user.LockedUntilTicks, user.LastLoginTicks, user.LockGeneration,
        }).ToListAsync(token).ConfigureAwait(false);
        return rows.Select(row => new UserDetails(row.Id, row.RoleId, row.MustChangePassword,
            row.LockedUntilTicks > now.UtcTicks ? new DateTimeOffset(row.LockedUntilTicks, TimeSpan.Zero) : null,
            row.LastLoginTicks is long ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null,
            row.LockedUntilTicks > now.UtcTicks ? row.LockGeneration.ToString(CultureInfo.InvariantCulture) : null)).ToArray();
    }

    public async Task<UnlockResult> UnlockAsync(UserSession actor, string userId, string lockToken, DateTimeOffset now, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = new IdentityDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        if (!await LockAndCheckAdministratorAsync(db, actor, token).ConfigureAwait(false)) return UnlockResult.Forbidden;
        var target = db.Users.Where(u => u.Id == userId);
        if (long.TryParse(lockToken, NumberStyles.None, CultureInfo.InvariantCulture, out var generation))
        {
            await target.Where(u => u.LockGeneration == generation && u.LockedUntilTicks > now.UtcTicks)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.LockedUntilTicks, 0L)
                    .SetProperty(u => u.Version, u => u.Version + 1), token).ConfigureAwait(false);
        }
        var until = await target.Select(u => (long?)u.LockedUntilTicks).SingleOrDefaultAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return until is null ? UnlockResult.NotFound : until > now.UtcTicks ? UnlockResult.LockChanged : UnlockResult.Unlocked;
    }

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
            var recordLogin = matches && (user.LastLoginTicks is null || user.LastLoginTicks < now.UtcTicks);
            if (recordLogin) user.LastLoginTicks = now.UtcTicks;
            if (recordLogin || state != next)
            {
                ApplyLoginState(user, next, result);
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

    private static void ApplyLoginState(UserRow user, SetupLoginState next, SetupLoginResult result)
    {
        user.Failures = next.Failures;
        user.LockedUntilTicks = next.LockedUntilTicks;
        if (result == SetupLoginResult.Locked) user.LockGeneration++;
    }

    public async Task<PasswordChangeResult> ChangeOwnPasswordAsync(UserSession session, string current, string password,
        int minimumLength, DateTimeOffset now, int limit, TimeSpan lockDuration, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockDuration, TimeSpan.Zero);
        while (true)
        {
            await using var db = new IdentityDbContext(_options);
            var user = await db.Users.FindAsync([session.UserId], token).ConfigureAwait(false);
            if (user is null || user.MustChangePassword || Session(user) != session) return new PasswordChangeResult.Expired();
            if (now.UtcTicks < user.LockedUntilTicks) return new PasswordChangeResult.Locked();
            var matches = _hasher.VerifyHashedPassword(user, user.PasswordHash, current) != PasswordVerificationResult.Failed;
            var (result, next) = new SetupLoginState(user.Failures, user.LockedUntilTicks).Check(matches, now, limit, lockDuration);
            ApplyLoginState(user, next, result);
            PasswordChangeResult outcome;
            switch (result)
            {
                case SetupLoginResult.Locked: outcome = new PasswordChangeResult.Locked(); break;
                case SetupLoginResult.Denied: outcome = new PasswordChangeResult.Incorrect(); break;
                case SetupLoginResult.Allowed:
                    if (current == password) outcome = new PasswordChangeResult.Unchanged();
                    else if (new StringInfo(password).LengthInTextElements < minimumLength) outcome = new PasswordChangeResult.TooShort();
                    else
                    {
                        user.PasswordHash = _hasher.HashPassword(user, password);
                        outcome = new PasswordChangeResult.Changed(Session(user));
                    }
                    break;
                default: throw new InvalidOperationException("未対応のパスワード照合結果です。");
            }
            user.Version++;
            try { await db.SaveChangesAsync(token).ConfigureAwait(false); }
            catch (DbUpdateConcurrencyException) { continue; }
            return outcome;
        }
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
