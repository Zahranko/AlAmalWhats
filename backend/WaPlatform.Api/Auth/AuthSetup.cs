using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Auth;

public class AuthOptions
{
    /// <summary>How long an access token is good for; the browser then renews it with the refresh token.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>A browser stays signed in this long after it was last used.</summary>
    public int RefreshTokenDays { get; set; } = 14;
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
    public const string LoginRateLimit = "login";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.Configure<AuthOptions>(config.GetSection("Auth"));

        services.AddSingleton<JwtKeyProvider>();
        services.AddScoped<TokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Configured here rather than inline so the signing key can come from the database.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<JwtKeyProvider>((o, keys) =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = TokenService.Issuer,
                ValidAudience = TokenService.Issuer,
                IssuerSigningKeyResolver = (_, _, _, _) => [keys.Key],
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role,
            };
            o.Events = new JwtBearerEvents
            {
                // The access token travels in an HttpOnly cookie, not an Authorization header.
                OnMessageReceived = ctx =>
                {
                    ctx.Token = ctx.Request.Cookies[TokenService.AccessCookie];
                    return Task.CompletedTask;
                },
                // Re-check the user on every request so disabling a user, changing their role
                // or resetting their password logs them out immediately.
                OnTokenValidated = async ctx =>
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var stamp = ctx.Principal?.FindFirstValue(AppClaims.SecurityStamp);
                    var valid = int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                        && await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsActive && u.SecurityStamp == stamp);
                    if (!valid) ctx.Fail("Session ended.");
                },
                // An API: a bare 401 (the frontend then tries /api/auth/refresh), no WWW-Authenticate details.
                OnChallenge = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                },
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

    public static ClaimsIdentity CreateIdentity(User user) =>
        new(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(AppClaims.SecurityStamp, user.SecurityStamp),
            new Claim(AppClaims.MustChangePassword, user.MustChangePassword ? "1" : "0"),
        ], JwtBearerDefaults.AuthenticationScheme);

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
