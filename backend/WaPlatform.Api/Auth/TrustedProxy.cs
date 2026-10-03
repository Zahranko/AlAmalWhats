using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WaPlatform.Api.Auth;

/// <summary>
/// The Next.js frontend runs on a different server and forwards /api/* here, so every request
/// arrives from its IP. When a request carries the shared Proxy:Secret, trust the client IP the
/// frontend passed along, so audit logs and the login rate limit see the real user.
/// Without the secret the header is ignored, so callers can't spoof their IP.
/// </summary>
public static class TrustedProxy
{
    public const string SecretHeader = "X-Wa-Proxy-Secret";
    public const string ClientIpHeader = "X-Wa-Client-Ip";

    public static IApplicationBuilder UseTrustedProxyClientIp(this IApplicationBuilder app, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return app;
        var expected = Encoding.UTF8.GetBytes(secret);

        return app.Use(async (ctx, next) =>
        {
            var given = ctx.Request.Headers[SecretHeader].ToString();
            if (given.Length > 0
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), expected)
                && IPAddress.TryParse(ctx.Request.Headers[ClientIpHeader].ToString(), out var ip))
            {
                ctx.Connection.RemoteIpAddress = ip;
            }
            ctx.Request.Headers.Remove(SecretHeader);
            ctx.Request.Headers.Remove(ClientIpHeader);
            await next();
        });
    }
}
