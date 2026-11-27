using System.Text.Json;
using FlowHub.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Identity.Infrastructure;

internal sealed class RoleRow
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public bool IdleTimeoutInitialized { get; set; }
    public long IdleTimeoutTicks { get; set; }
    public List<RolePermissionRow> Permissions { get; } = [];
}

internal sealed class RolePermissionRow
{
    public required string RoleId { get; init; }
    public required string Permission { get; init; }
}

public sealed record RoleDetails(string Id, string Name, bool Fixed, bool IsDefault, TimeSpan IdleTimeout, IReadOnlyList<string> Permissions)
{
    public long IdleTimeoutMinutes => IdleTimeout.Ticks / TimeSpan.TicksPerMinute;
}

public enum DefaultRoleResult { Changed, Administrator, NotFound, Forbidden }

public enum SaveRoleResult { Saved, InvalidName, InvalidTimeout, Duplicate, NotFound, Forbidden }

public enum DeleteRoleResult { Deleted, NotFound, Protected, Forbidden }

public sealed class RoleDirectory
{
    private readonly DbContextOptions<IdentityDbContext> _options;
    internal RoleDirectory(DbContextOptions<IdentityDbContext> options) => _options = options;

    public async Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken token)
    {
        await using var db = new IdentityDbContext(_options);
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).ToListAsync(token).ConfigureAwait(false);
        return roles.Select(row => new RoleDetails(row.Id, row.Name, row.Id == InitialRoles.Administrator.Id,
            row.IsDefault, TimeSpan.FromTicks(row.IdleTimeoutTicks), row.Permissions.Select(p => p.Permission).ToArray())).ToArray();
    }

    internal static async Task<DefaultRoleResult> SetDefaultAsync(IdentityDbContext db, string roleId, CancellationToken token)
    {
        var kind = roleId == InitialRoles.Administrator.Id ? RoleKind.Administrator : RoleKind.Editable;
        if (!RoleRules.CanBeDefault(kind)) return DefaultRoleResult.Administrator;
        var changed = await db.Roles
            .Where(role => (role.IsDefault || role.Id == roleId) && db.Roles.Any(candidate => candidate.Id == roleId))
            .ExecuteUpdateAsync(update => update.SetProperty(role => role.IsDefault, role => role.Id == roleId), token)
            .ConfigureAwait(false);
        return changed == 0 ? DefaultRoleResult.NotFound : DefaultRoleResult.Changed;
    }

    internal static async Task InitializeAsync(IdentityDbContext db, CancellationToken token)
    {
        if (await db.Roles.AnyAsync(token).ConfigureAwait(false)) return;
        foreach (var role in new[] { InitialRoles.Viewer, InitialRoles.Operator, InitialRoles.Maintainer, InitialRoles.Administrator })
        {
            var row = new RoleRow { Id = role.Id, Name = role.Name, IsDefault = role.IsDefault, IdleTimeoutTicks = role.IdleTimeout.Ticks };
            foreach (var permission in role.Permissions)
            {
                row.Permissions.Add(new RolePermissionRow { RoleId = role.Id, Permission = JsonNamingPolicy.CamelCase.ConvertName(permission.ToString()) });
            }
            db.Roles.Add(row);
        }
        try
        {
            await db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // 別プロセスが先に初期化をコミットした場合だけ、その状態を使う。
            if (!await db.Roles.AnyAsync(r => r.Id == InitialRoles.Administrator.Id, token).ConfigureAwait(false)) throw;
        }
    }
}
