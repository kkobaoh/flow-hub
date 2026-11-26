using FlowHub.Identity.Infrastructure;
using FlowHub.Identity;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlowHub.Api.Composition;

internal static class SetupEndpoints
{
    internal const string Scheme = "FlowHub.Setup";
    private const string Policy = "CreateFirstAdministrator";
    internal sealed record CreateRequest(string UserId);
    internal sealed class LoginRequest
    {
        public required string Password { get; init; }
    }

    internal static void AddServices(IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = Scheme;
            options.Cookie.Path = "/identity/setup";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Events.OnValidatePrincipal = context =>
            {
                context.ShouldRenew = true;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });
        services.AddOptions<CookieAuthenticationOptions>(Scheme).Configure<IConfiguration, IWebHostEnvironment>((options, config, environment) =>
        {
            options.ExpireTimeSpan = config.GetValue("Identity:Setup:IdleTimeout", TimeSpan.FromHours(8));
            if (environment.IsDevelopment()) options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        services.AddAuthorizationBuilder().AddPolicy(Policy, policy =>
            policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim("purpose", "initial-setup"));
        services.AddAntiforgery(options => options.HeaderName = "X-FlowHub-CSRF");
    }

    internal static void Map(WebApplication app, FirstAdministratorStore store, TimeProvider clock)
    {
        var failureLimit = app.Configuration.GetValue("Identity:LoginFailureLimit", 5);
        var lockDuration = app.Configuration.GetValue("Identity:LockDuration", TimeSpan.FromMinutes(15));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failureLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lockDuration, TimeSpan.Zero);
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/identity"), branch => branch.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await next(context).ConfigureAwait(false);
        }));
        app.MapGet("/identity/setup/login-form", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/identity/setup/login", async (LoginRequest request, HttpContext context, IAntiforgery antiforgery, CancellationToken token) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false)) return Results.BadRequest();
            var configured = app.Configuration["Identity:SetupPassword"];
            var matches = !string.IsNullOrEmpty(configured) && request.Password is not null &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(request.Password)));
            var result = await store.CheckLoginAsync(matches, clock.GetUtcNow(), failureLimit, lockDuration, token).ConfigureAwait(false);
            switch (result)
            {
                case SetupLoginResult.Completed: return Results.Conflict();
                case SetupLoginResult.Locked: return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                case SetupLoginResult.Denied: return Results.Unauthorized();
                case SetupLoginResult.Allowed:
                    var principal = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "setup"), new Claim("purpose", "initial-setup")], Scheme));
                    await context.SignInAsync(Scheme, principal).ConfigureAwait(false);
                    return Results.NoContent();
                default: throw new InvalidOperationException("未対応の初期設定のログイン結果です。");
            }
        });
        var group = app.MapGroup("/identity/setup").RequireAuthorization(Policy);
        group.MapGet("/form", async (HttpContext context, IAntiforgery antiforgery, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (await store.IsSetupCompleteAsync(token).ConfigureAwait(false))
            {
                return Results.Conflict();
            }

            return Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        group.MapPost("/administrator", async (CreateRequest request, HttpContext context, IAntiforgery antiforgery, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
            {
                return Results.BadRequest();
            }

            FirstAdministratorResult result;
            try
            {
                result = await store.CreateAsync(request.UserId, token).ConfigureAwait(false);
            }
            catch (ArgumentException ex) when (ex.ParamName == "input")
            {
                return Results.BadRequest();
            }

            await context.SignOutAsync(Scheme).ConfigureAwait(false);
            return result switch
            {
                FirstAdministratorResult.Created created => Results.Ok(new { created.TemporaryPassword }),
                FirstAdministratorResult.AlreadyCompleted => Results.Conflict(),
                _ => throw new InvalidOperationException("未対応の初期設定結果です。"),
            };
        });
    }
}
