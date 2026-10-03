using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;

namespace WaPlatform.Api.Auth;

/// <summary>
/// "X-Api-Key: wak_..." authentication for /api/v1, used by the hospital's other systems.
/// Keys are random 256-bit values; only their SHA-256 is stored.
/// </summary>
public class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string Header = "X-Api-Key";
    public const string KeyIdClaim = "api_key_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(key)) return AuthenticateResult.NoResult();

        var hash = Hash(key);
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null);
        if (apiKey is null) return AuthenticateResult.Fail("Invalid or revoked API key.");

        // Record use at most once a minute to avoid a write per request.
        if (apiKey.LastUsedAt is null || apiKey.LastUsedAt < DateTime.UtcNow.AddMinutes(-1))
            await db.ApiKeys.Where(k => k.Id == apiKey.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));

        var identity = new ClaimsIdentity(
        [
            new Claim(KeyIdClaim, apiKey.Id.ToString()),
            new Claim(ClaimTypes.Name, $"API key: {apiKey.Name}"),
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new { error = $"Send a valid API key in the {Header} header." });
    }

    public static string NewKey() => "wak_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
