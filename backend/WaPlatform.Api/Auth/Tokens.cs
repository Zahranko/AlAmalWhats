using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Auth;

/// <summary>
/// The key access tokens are signed with. Auth:JwtKey (base64, at least 32 bytes) if configured;
/// otherwise one is generated once and kept in the settings table, encrypted with Data Protection,
/// so every app instance and restart shares it without extra configuration.
/// </summary>
public class JwtKeyProvider(IServiceScopeFactory scopes, IConfiguration config)
{
    private const string SettingKey = "auth.jwt_key";
    private readonly Lazy<SymmetricSecurityKey> key = new(() => Load(scopes, config));

    public SymmetricSecurityKey Key => key.Value;

    private static SymmetricSecurityKey Load(IServiceScopeFactory scopes, IConfiguration config)
    {
        if (config["Auth:JwtKey"] is { Length: > 0 } configured)
            return new SymmetricSecurityKey(Convert.FromBase64String(configured));

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("WaPlatform.JwtKey");

        var row = db.Settings.AsNoTracking().SingleOrDefault(s => s.Key == SettingKey);
        if (row is null)
        {
            db.Settings.Add(new AppSetting { Key = SettingKey, Value = protector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))) });
            try { db.SaveChanges(); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); } // another instance created it first
            row = db.Settings.AsNoTracking().Single(s => s.Key == SettingKey);
        }
        return new SymmetricSecurityKey(Convert.FromBase64String(protector.Unprotect(row.Value)));
    }
}

public enum RefreshOutcome { Renewed, Rejected }

/// <summary>
/// Issues the two session cookies: a short-lived signed access token (JWT) read on every request,
/// and a long-lived refresh token, only sent to /api/auth, that buys a new pair.
/// </summary>
public class TokenService(AppDbContext db, JwtKeyProvider keys, IOptions<AuthOptions> options, IHttpContextAccessor http,
    IHostEnvironment env, ILogger<TokenService> log)
{
    public const string AccessCookie = "wa_access";
    public const string RefreshCookie = "wa_refresh";
    public const string RefreshPath = "/api/auth";
    public const string Issuer = "WaPlatform";

    /// <summary>
    /// A token exchanged this recently may be presented again without counting as theft: two tabs
    /// refreshing at once, or a response lost on the way back, would otherwise sign the user out.
    /// </summary>
    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(60);

    private AuthOptions O => options.Value;
    private HttpContext Ctx => http.HttpContext!;

    /// <summary>Signs the user in: a new session family, or the given one when renewing.</summary>
    public async Task IssueAsync(User user, string? familyId = null)
    {
        var now = DateTime.UtcNow;
        var refresh = NewToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refresh),
            FamilyId = familyId ?? Guid.NewGuid().ToString("N"),
            SecurityStamp = user.SecurityStamp,
            CreatedAt = now,
            ExpiresAt = now.AddDays(O.RefreshTokenDays),
            CreatedByIp = Ctx.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Subject = AuthSetup.CreateIdentity(user),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(O.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.HmacSha256),
        });

        // The access cookie outlives its token so the frontend can tell "signed in, token expired"
        // (refresh) from "never signed in" (login page); the token inside is what counts.
        var lifetime = TimeSpan.FromDays(O.RefreshTokenDays);
        Ctx.Response.Cookies.Append(AccessCookie, access, CookieOptions("/", lifetime));
        Ctx.Response.Cookies.Append(RefreshCookie, refresh, CookieOptions(RefreshPath, lifetime));
    }

    /// <summary>Exchanges the refresh cookie for a new pair. Rejected means the session is over.</summary>
    public async Task<(RefreshOutcome Outcome, User? User)> RefreshAsync()
    {
        var presented = Ctx.Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(presented)) return (RefreshOutcome.Rejected, null);

        var now = DateTime.UtcNow;
        var hash = Hash(presented);
        var token = await db.RefreshTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null || token.ExpiresAt <= now || token.RevokedAt is not null) return (RefreshOutcome.Rejected, null);

        if (token.UsedAt is { } used)
        {
            if (now - used > ReuseGrace)
            {
                // An exchanged token came back long after: someone kept a copy. End the whole session.
                log.LogWarning("Refresh token reused for user {UserId}; revoking the session.", token.UserId);
                await RevokeFamilyAsync(token.FamilyId);
                return (RefreshOutcome.Rejected, null);
            }
        }
        else
        {
            token.UsedAt = now;
        }

        var user = token.User!;
        if (!user.IsActive || user.SecurityStamp != token.SecurityStamp)
        {
            await db.SaveChangesAsync();
            return (RefreshOutcome.Rejected, null);
        }

        await IssueAsync(user, token.FamilyId);
        return (RefreshOutcome.Renewed, user);
    }

    /// <summary>Signs this browser out: revokes its session and removes both cookies.</summary>
    public async Task SignOutAsync()
    {
        await RevokeCurrentAsync();
        ClearCookies();
    }

    /// <summary>Revokes the session this browser's refresh cookie belongs to.</summary>
    public async Task RevokeCurrentAsync()
    {
        if (Ctx.Request.Cookies[RefreshCookie] is not { Length: > 0 } presented) return;
        var hash = Hash(presented);
        var family = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => t.FamilyId).FirstOrDefaultAsync();
        if (family is not null) await RevokeFamilyAsync(family);
    }

    public void ClearCookies()
    {
        Ctx.Response.Cookies.Delete(AccessCookie, CookieOptions("/", null));
        Ctx.Response.Cookies.Delete(RefreshCookie, CookieOptions(RefreshPath, null));
    }

    // Exchanged tokens are revoked too, so none of them gets the reuse grace any more.
    private Task RevokeFamilyAsync(string familyId) =>
        db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));

    private CookieOptions CookieOptions(string path, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        // Dev runs over plain http behind the Next.js dev server; production must be HTTPS.
        Secure = !env.IsDevelopment() || Ctx.Request.IsHttps,
        Path = path,
        MaxAge = maxAge,
        IsEssential = true,
    };

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
