using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

public record WhatsAppStatusDto(bool Configured, List<string> Missing, PhoneNumberInfo? PhoneNumber, string? Error);
public record TestMessageRequest(
    [Required, RegularExpression(@"^\+?[1-9]\d{6,14}$")] string To,
    [RegularExpression("^[a-z0-9_]{1,512}$")] string? Template = null,
    [RegularExpression("^[a-z]{2,3}(_[A-Z]{2})?$")] string? Language = null);
public record TestMessageResult(string MessageId);

[ApiController]
[Route("api/whatsapp")]
[Authorize(Roles = Roles.Admin)]
public class WhatsAppController(
    WhatsAppClient client, IOptionsMonitor<WhatsAppOptions> options, AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Shows which settings are missing and whether Meta accepts the token.</summary>
    [HttpGet("status")]
    public async Task<WhatsAppStatusDto> Status(CancellationToken ct)
    {
        var missing = options.CurrentValue.MissingSettings().ToList();
        try
        {
            var phone = await client.GetPhoneNumberAsync(ct);
            return new WhatsAppStatusDto(missing.Count == 0, missing, phone, null);
        }
        catch (WhatsAppApiException ex)
        {
            return new WhatsAppStatusDto(false, missing, null, ex.Message);
        }
    }

    /// <summary>
    /// Sends an approved template without parameters (default: Meta's "hello_world", en_US).
    /// With a test number the recipient must be one of the numbers added on the API Setup page.
    /// </summary>
    [HttpPost("test-message")]
    public async Task<ActionResult<TestMessageResult>> SendTest(TestMessageRequest req, CancellationToken ct)
    {
        var to = req.To.TrimStart('+');
        try
        {
            var template = req.Template ?? "hello_world";
            var id = await client.SendTemplateAsync(to, template, req.Language ?? "en_US", ct);
            audit.Record(AuditActions.WhatsAppTestSent, to, new { messageId = id, template });
            await db.SaveChangesAsync(ct);
            return new TestMessageResult(id);
        }
        catch (WhatsAppApiException ex)
        {
            return StatusCode(ex.StatusCode is >= 400 and < 500 ? 400 : 502, new ApiError(ex.Message));
        }
    }
}
