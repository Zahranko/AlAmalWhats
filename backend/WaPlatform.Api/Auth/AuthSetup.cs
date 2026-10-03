using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Auth;

public class AuthOptions
{
    public int SessionMinutes { get; set; } = 30;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public static class AppClaims
{
    public const string SecurityStamp = "stamp";
    public const string MustChangePassword = "must_change_password";
}

public static class AuthSetup
{
    public const string CookieName = "wa_session";
    public const string LoginRateLimit = "login";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var opts = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(config.GetSection("Auth"));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                // Dev runs over plain http behind the Next.js dev server; production must be HTTPS.
                o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(opts.SessionMinutes);
                o.SlidingExpiration = true;

                // This is an API: answer with status codes instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };

                // Re-check the user on every request so disabling a user, changing their role
                // or resetting their password logs them out immediately.
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var stamp = ctx.Principal?.FindFirstValue(AppClaims.SecurityStamp);
                    var valid = int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                        && await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsActive && u.SecurityStamp == stamp);
                    if (!valid)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorizationBuilder()
            // Every endpoint requires a signed-in user unless marked [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(User user) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(AppClaims.SecurityStamp, user.SecurityStamp),
            new Claim(AppClaims.MustChangePassword, user.MustChangePassword ? "1" : "0"),
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>
    /// Users created or reset by an admin must set their own password before using anything
    /// other than the /api/auth endpoints.
    /// </summary>
    public static IApplicationBuilder UseRequirePasswordChange(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.User.FindFirstValue(AppClaims.MustChangePassword) == "1"
                && !ctx.Request.Path.StartsWithSegments("/api/auth"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { error = "You must change your password first.", code = "password_change_required" });
                return;
            }
            await next();
        });

    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
