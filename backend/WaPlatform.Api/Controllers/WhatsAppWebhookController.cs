using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

/// <summary>
/// Callback URL for the Meta dashboard: https://&lt;public host&gt;/api/webhooks/whatsapp
/// </summary>
[ApiController]
[Route("api/webhooks/whatsapp")]
[AllowAnonymous]
public class WhatsAppWebhookController(
    AppDbContext db, IOptionsMonitor<WhatsAppOptions> options, ILogger<WhatsAppWebhookController> log) : ControllerBase
{
    /// <summary>Meta's subscription check: echo hub.challenge if the verify token matches.</summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = options.CurrentValue.VerifyToken;
        if (string.IsNullOrEmpty(expected))
        {
            log.LogError("Webhook verification attempted but WhatsApp:VerifyToken is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (mode != "subscribe" || challenge is null || !FixedTimeEquals(verifyToken ?? "", expected))
        {
            log.LogWarning("Webhook verification rejected (mode={Mode}).", mode);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Content(challenge, "text/plain");
    }

    /// <summary>Event delivery. Verify the signature, store the raw payload, acknowledge fast.</summary>
    [HttpPost]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var secret = options.CurrentValue.AppSecret;
        if (string.IsNullOrEmpty(secret))
        {
            // 503 makes Meta retry, so nothing is lost once the secret is configured.
            log.LogError("Webhook event received but WhatsApp:AppSecret is not configured; rejecting.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();

        if (!SignatureValid(body, Request.Headers["X-Hub-Signature-256"].ToString(), secret))
        {
            log.LogWarning("Webhook event with missing or invalid signature from {Ip}.", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        db.WebhookEvents.Add(new WebhookEvent { Payload = Encoding.UTF8.GetString(body) });
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private static bool SignatureValid(byte[] body, string header, string secret)
    {
        const string prefix = "sha256=";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return false;

        byte[] given;
        try { given = Convert.FromHexString(header.AsSpan(prefix.Length)); }
        catch (FormatException) { return false; }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
