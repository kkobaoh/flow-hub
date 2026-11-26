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
    private const string Ready = "UserPasswordChanged";

    internal sealed class Credentials
    {
        public required string UserId { get; init; }
        public required string Password { get; init; }
    }

    internal sealed class NewPassword
    {
        public required string Password { get; init; }
    }

    internal static void AddServices(IServiceCollection services, DatabaseOptions database)
    {
        services.AddSingleton(IdentityDatabase.OpenUserLogins(builder => Databases.Configure(builder, database, IdentityDatabase.Schema)));
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = Scheme;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Events.OnValidatePrincipal = async context =>
            {
                var session = ReadTicket(context.Principal);
                if (session is null || !await context.HttpContext.RequestServices.GetRequiredService<UserLoginStore>()
                        .IsCurrentAsync(session, context.HttpContext.RequestAborted).ConfigureAwait(false))
                {
                    context.RejectPrincipal();
                    return;
                }
                context.ShouldRenew = true;
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

    internal static void Map(WebApplication app, TimeProvider clock)
    {
        var limit = app.Configuration.GetValue("Identity:LoginFailureLimit", 5);
        var duration = app.Configuration.GetValue("Identity:LockDuration", TimeSpan.FromMinutes(15));
        var minimum = app.Configuration.GetValue("Identity:MinimumPasswordLength", 8);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        var group = app.MapGroup("/identity/user");
        group.MapGet("/form", (HttpContext context, IAntiforgery csrf) =>
            Results.Ok(new { requestToken = csrf.GetAndStoreTokens(context).RequestToken }));
        group.MapPost("/login", async (Credentials input, UserLoginStore store, HttpContext context, IAntiforgery csrf, CancellationToken token) =>
        {
            if (!await csrf.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var result = await store.LoginAsync(input.UserId, input.Password, clock.GetUtcNow(), limit, duration, token).ConfigureAwait(false);
            return result switch
            {
                UserLoginResult.Accepted accepted => await SignInAsync(context, accepted.Session).ConfigureAwait(false),
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
                PasswordChangeResult.Changed changed => await SignInAsync(context, changed.Session).ConfigureAwait(false),
                PasswordChangeResult.Expired => Results.Unauthorized(),
                PasswordChangeResult.Unchanged => Results.BadRequest(new { reason = "unchanged" }),
                PasswordChangeResult.TooShort => Results.BadRequest(new { reason = "tooShort", minimumLength = minimum }),
                _ => throw new InvalidOperationException("未対応のパスワード変更結果です。"),
            };
        }).RequireAuthorization(SignedIn);
        group.MapGet("/account", (ClaimsPrincipal user) => Results.Ok(new { userId = user.FindFirstValue(ClaimTypes.NameIdentifier) }))
            .RequireAuthorization(Ready);
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

    private static async Task<IResult> SignInAsync(HttpContext context, UserSession session)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, session.UserId),
            new Claim("credential", session.CredentialStamp), new Claim("stage", session.MustChangePassword ? "temporary" : "ready")], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity)).ConfigureAwait(false);
        return Results.Ok(new { session.UserId, session.MustChangePassword });
    }

    private static Task Status(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
