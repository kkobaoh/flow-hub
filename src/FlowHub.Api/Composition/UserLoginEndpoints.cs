using System.Security.Claims;
using FlowHub.Api.Database;
using FlowHub.Identity.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlowHub.Api.Composition;

internal static class UserLoginEndpoints
{
    private const string Scheme = "FlowHub.User";
    private const string SignedIn = "UserSignedIn";
    internal const string Ready = "UserPasswordChanged";

    internal sealed class Credentials
    {
        public required string UserId { get; init; }
        public required string Password { get; init; }
    }

    internal sealed class NewPassword
    {
        public required string Password { get; init; }
    }

    internal sealed record RoleChange(string UserId, string RoleId);
    internal sealed record RoleSave(string? RoleId, string? Name, string? Minutes);
    internal sealed record RoleRequest(string RoleId);
    internal sealed record OwnPassword(string CurrentPassword, string Password);
    internal sealed record UnlockRequest(string UserId, string LockToken);

    internal static void AddServices(IServiceCollection services, DatabaseOptions database)
    {
        services.AddSingleton(IdentityDatabase.OpenUserLogins(builder => Databases.Configure(builder, database, IdentityDatabase.Schema)));
        services.AddSingleton(IdentityDatabase.OpenRoles(builder => Databases.Configure(builder, database, IdentityDatabase.Schema)));
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = Scheme;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                var session = ReadTicket(context.Principal);
                var store = context.HttpContext.RequestServices.GetRequiredService<UserLoginStore>();
                var token = context.HttpContext.RequestAborted;
                if (session is null || context.Properties.IssuedUtc is not { } last || !await store.IsCurrentAsync(session, token).ConfigureAwait(false))
                {
                    context.RejectPrincipal();
                    return;
                }
                var lifetime = await store.GetIdleTimeoutAsync(session.UserId, token).ConfigureAwait(false);
                var now = context.Options.TimeProvider!.GetUtcNow();
                if (now - last >= lifetime)
                {
                    context.RejectPrincipal();
                    return;
                }
                context.Properties.IssuedUtc = now;
                context.Properties.ExpiresUtc = Expires(now, lifetime);
                // 同じ要求でログアウトやパスワード変更が行われた場合は、その Cookie を優先する。
                context.Response.OnStarting(() => context.Response.Headers.SetCookie.Any(
                    value => value?.StartsWith(Scheme + "=", StringComparison.Ordinal) == true)
                    ? Task.CompletedTask
                    : context.HttpContext.SignInAsync(Scheme, context.Principal!, context.Properties));
            };
            options.Events.OnRedirectToLogin = context => Status(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => Status(context, StatusCodes.Status403Forbidden);
        });
        services.AddOptions<CookieAuthenticationOptions>(Scheme).Configure<IConfiguration, IWebHostEnvironment>((options, config, environment) =>
        {
            options.ExpireTimeSpan = config.GetValue("Identity:Administrator:IdleTimeout", TimeSpan.FromHours(8));
            if (environment.IsDevelopment()) options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(SignedIn, p => p.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser())
            .AddPolicy(Ready, p => p.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim("stage", "ready"));
    }

    internal static RouteGroupBuilder RequireScreenCommand(this RouteGroupBuilder group, string permission, string? alternative = null)
    {
        group.RequireAuthorization(Ready);
        return group.AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            var store = http.RequestServices.GetRequiredService<UserLoginStore>();
            var account = await store.GetCurrentAccountAsync(ReadTicket(http.User)!, http.RequestAborted).ConfigureAwait(false);
            if (account is null) return Results.Unauthorized();
            if (!account.Permissions.Contains(permission) && (alternative is null || !account.Permissions.Contains(alternative)))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var csrf = http.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await csrf.IsRequestValidAsync(http).ConfigureAwait(false)) return Results.BadRequest();
            if (alternative is not null) http.Features.Set(account);
            return await next(invocation).ConfigureAwait(false);
        });
    }

    internal static void Map(WebApplication app, TimeProvider clock)
    {
        var limit = app.Configuration.GetValue("Identity:LoginFailureLimit", 5);
        var duration = app.Configuration.GetValue("Identity:LockDuration", TimeSpan.FromMinutes(15));
        var minimum = app.Configuration.GetValue("Identity:MinimumPasswordLength", 8);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        app.Use(async (context, next) =>
        {
            // 認可ポリシーを持たない自動取得や SignalR のポーリングでも、その端末の認証 Cookie を更新する。
            if (context.Request.Cookies.ContainsKey(Scheme))
                await context.AuthenticateAsync(Scheme).ConfigureAwait(false);
            await next(context).ConfigureAwait(false);
        });
        var group = app.MapGroup("/identity/user");
        group.MapGet("/form", (HttpContext context, IAntiforgery csrf) =>
            Results.Ok(new { requestToken = csrf.GetAndStoreTokens(context).RequestToken }));
        group.MapPost("/login", async (Credentials input, UserLoginStore store, HttpContext context, IAntiforgery csrf, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.LoginAsync(input.UserId, input.Password, clock.GetUtcNow(), limit, duration, token).ConfigureAwait(false);
            return result switch
            {
                UserLoginResult.Accepted accepted => await SignInAsync(context, accepted.Session, store, clock, token).ConfigureAwait(false),
                UserLoginResult.Denied => Results.Unauthorized(),
                UserLoginResult.Locked => Results.StatusCode(StatusCodes.Status429TooManyRequests),
                _ => throw new InvalidOperationException("未対応のログイン結果です。"),
            };
        });
        group.MapGet("/session", (HttpContext context, IAntiforgery csrf) =>
        {
            var session = ReadTicket(context.User)!;
            return Results.Ok(new { session.UserId, session.MustChangePassword, minimumLength = minimum, requestToken = csrf.GetAndStoreTokens(context).RequestToken });
        }).RequireAuthorization(SignedIn);
        group.MapPost("/initial-password", async (NewPassword input, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (input.Password is null || !await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.ChangeInitialPasswordAsync(ReadTicket(context.User)!, input.Password, minimum, token).ConfigureAwait(false);
            return result switch
            {
                PasswordChangeResult.Changed changed => await SignInAsync(context, changed.Session, store, clock, token).ConfigureAwait(false),
                PasswordChangeResult.Expired => Results.Unauthorized(),
                PasswordChangeResult.Unchanged => Results.BadRequest(new { reason = "unchanged" }),
                PasswordChangeResult.TooShort => Results.BadRequest(new { reason = "tooShort", minimumLength = minimum }),
                _ => throw new InvalidOperationException("未対応のパスワード変更結果です。"),
            };
        }).RequireAuthorization(SignedIn);
        group.MapPost("/password", async (OwnPassword input, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (input.CurrentPassword is null || input.Password is null || !await csrf.IsRequestValidAsync(context).ConfigureAwait(false))
                return Results.BadRequest();
            var result = await store.ChangeOwnPasswordAsync(ReadTicket(context.User)!, input.CurrentPassword, input.Password,
                minimum, clock.GetUtcNow(), limit, duration, token).ConfigureAwait(false);
            return result switch
            {
                PasswordChangeResult.Changed changed => await SignInAsync(context, changed.Session, store, clock, token).ConfigureAwait(false),
                PasswordChangeResult.Expired => Results.Unauthorized(),
                PasswordChangeResult.Incorrect => Results.BadRequest(new { reason = "incorrect" }),
                PasswordChangeResult.Unchanged => Results.BadRequest(new { reason = "unchanged" }),
                PasswordChangeResult.TooShort => Results.BadRequest(new { reason = "tooShort", minimumLength = minimum }),
                PasswordChangeResult.Locked => Results.StatusCode(StatusCodes.Status429TooManyRequests),
                _ => throw new InvalidOperationException("未対応のパスワード変更結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapGet("/account", async (ClaimsPrincipal user, UserLoginStore store, CancellationToken token) =>
        {
            var account = await store.GetCurrentAccountAsync(ReadTicket(user)!, token).ConfigureAwait(false);
            return account is null ? Results.Unauthorized() : Results.Ok(account);
        }).RequireAuthorization(Ready);
        group.MapGet("/roles", (RoleDirectory directory, CancellationToken token) => directory.ListAsync(token))
            .RequireAuthorization(Ready);
        group.MapGet("/users", (UserLoginStore store, CancellationToken token) => store.ListAsync(clock.GetUtcNow(), token))
            .RequireAuthorization(Ready);
        group.MapPost("/users", async (SetupEndpoints.CreateRequest request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.AddAsync(ReadTicket(context.User)!, request.UserId, token).ConfigureAwait(false);
            return result switch
            {
                AddUserResult.Created created => Results.Ok(new { created.TemporaryPassword }),
                AddUserResult.Duplicate => Results.Conflict(),
                AddUserResult.InvalidId => Results.BadRequest(),
                AddUserResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => throw new InvalidOperationException("未対応のユーザー追加結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/users/temporary-password", async (SetupEndpoints.CreateRequest request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.ResetPasswordAsync(ReadTicket(context.User)!, request.UserId, token).ConfigureAwait(false);
            return result switch
            {
                PasswordResetResult.Issued issued => Results.Ok(new { issued.TemporaryPassword }),
                PasswordResetResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                PasswordResetResult.NotFound => Results.NotFound(),
                _ => throw new InvalidOperationException("未対応の仮パスワード再発行結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/roles/permissions", async ([Microsoft.AspNetCore.Mvc.FromBody] PermissionChange[] changes, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.SavePermissionsAsync(ReadTicket(context.User)!, changes, token).ConfigureAwait(false);
            return result switch
            {
                SavePermissionsResult.Saved => Results.NoContent(),
                SavePermissionsResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                SavePermissionsResult.Invalid => Results.BadRequest(),
                SavePermissionsResult.NotFound => Results.NotFound(),
                _ => throw new InvalidOperationException("未対応のパーミッション保存結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/roles/save", async (RoleSave request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.SaveRoleAsync(ReadTicket(context.User)!, request.RoleId, request.Name, request.Minutes, clock.GetUtcNow(), token).ConfigureAwait(false);
            return result switch
            {
                SaveRoleResult.Saved => Results.NoContent(),
                SaveRoleResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                SaveRoleResult.InvalidName => Results.BadRequest("ロールの名前を確認してください。"),
                SaveRoleResult.InvalidTimeout => Results.BadRequest("無操作期限を認証で扱える範囲の正の整数で入力してください。"),
                SaveRoleResult.NotFound => Results.NotFound(),
                SaveRoleResult.Duplicate => Results.Conflict(),
                _ => throw new InvalidOperationException("未対応のロール保存結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/roles/delete", async (RoleRequest request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.DeleteRoleAsync(ReadTicket(context.User)!, request.RoleId, token).ConfigureAwait(false);
            return result switch
            {
                DeleteRoleResult.Deleted or DeleteRoleResult.NotFound => Results.NoContent(),
                DeleteRoleResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                DeleteRoleResult.Protected => Results.Conflict(),
                _ => throw new InvalidOperationException("未対応のロール削除結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/roles/default", async (RoleRequest request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.SetDefaultRoleAsync(ReadTicket(context.User)!, request.RoleId, token).ConfigureAwait(false);
            return result switch
            {
                DefaultRoleResult.Changed => Results.NoContent(),
                DefaultRoleResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                DefaultRoleResult.Administrator => Results.BadRequest(),
                DefaultRoleResult.NotFound => Results.NotFound(),
                _ => throw new InvalidOperationException("未対応のデフォルトロール変更結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/users/role", async (RoleChange request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.ChangeRoleAsync(ReadTicket(context.User)!, request.UserId, request.RoleId, token).ConfigureAwait(false);
            return result switch
            {
                RoleChangeResult.Changed => Results.NoContent(),
                RoleChangeResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                RoleChangeResult.NotFound => Results.NotFound(),
                RoleChangeResult.LastAdministrator => Results.Conflict(),
                _ => throw new InvalidOperationException("未対応のロール変更結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/users/unlock", async (UnlockRequest request, HttpContext context, IAntiforgery csrf, UserLoginStore store, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false) || string.IsNullOrEmpty(request.LockToken)) return Results.BadRequest();
            var result = await store.UnlockAsync(ReadTicket(context.User)!, request.UserId, request.LockToken, clock.GetUtcNow(), token).ConfigureAwait(false);
            return result switch
            {
                UnlockResult.Unlocked => Results.NoContent(),
                UnlockResult.LockChanged => Results.Conflict(),
                UnlockResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
                UnlockResult.NotFound => Results.NotFound(),
                _ => throw new InvalidOperationException("未対応のロック解除結果です。"),
            };
        }).RequireAuthorization(Ready);
        group.MapPost("/logout", async (HttpContext context, IAntiforgery csrf) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            await context.SignOutAsync(Scheme).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization(SignedIn);
    }

    private static UserSession? ReadTicket(ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal?.FindFirstValue("credential");
        var stage = principal?.FindFirstValue("stage");
        return id is null || stamp is null || stage is not ("ready" or "temporary") ? null : new(id, stamp, stage == "temporary");
    }

    private static async Task<IResult> SignInAsync(HttpContext context, UserSession session, UserLoginStore store, TimeProvider clock, CancellationToken token)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, session.UserId),
            new Claim("credential", session.CredentialStamp), new Claim("stage", session.MustChangePassword ? "temporary" : "ready")], Scheme);
        var lifetime = await store.GetIdleTimeoutAsync(session.UserId, token).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IssuedUtc = now, ExpiresUtc = Expires(now, lifetime),
        }).ConfigureAwait(false);
        return Results.Ok(new { session.UserId, session.MustChangePassword });
    }

    private static DateTimeOffset Expires(DateTimeOffset now, TimeSpan lifetime)
        => lifetime >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now.Add(lifetime);

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
