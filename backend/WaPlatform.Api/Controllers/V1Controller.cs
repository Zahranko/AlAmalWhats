using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

public record V1Document([Required, MaxLength(255)] string FileName, [Required] string ContentBase64);

public record V1SendRequest(
    [Required, MaxLength(30)] string Phone,
    [MaxLength(200)] string? Name,
    [Required, MaxLength(512)] string Template,
    [MaxLength(16)] string? Language,
    List<string>? Header,
    List<string>? Body,
    List<string>? Buttons,
    V1Document? Document,
    DateTime? ScheduledAt);

public record V1MessageStatus(long Id, string Status, string? Error, DateTime CreatedAt, DateTime? SentAt,
    DateTime? DeliveredAt, DateTime? ReadAt, DateTime? FailedAt);

/// <summary>
/// Automatic sending for the hospital's other systems (appointments, lab results...).
/// Authenticate with the "X-Api-Key" header; keys are created by an admin in Settings.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = ApiKeyHandler.SchemeName)]
[EnableRateLimiting(ApiKeyHandler.SchemeName)]
public class V1Controller(AppDbContext db, MessageService messages) : ControllerBase
{
    /// <summary>Approved templates and the variables each one needs.</summary>
    [HttpGet("templates")]
    public async Task<List<TemplateDto>> Templates(CancellationToken ct)
    {
        var list = await db.Templates.AsNoTracking().Where(t => t.Status == TemplateStatuses.Approved)
            .OrderBy(t => t.Name).ToListAsync(ct);
        return list.Select(TemplateDto.From).ToList();
    }

    /// <summary>Queues a template message. Returns 202 with the message id; poll GET messages/{id} for delivery.</summary>
    [HttpPost("messages")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<V1MessageStatus>> Send(V1SendRequest req, CancellationToken ct)
    {
        var phone = messages.NormalizePhone(req.Phone);
        if (phone is null) return BadRequest(new ApiError("Invalid phone number."));

        var templates = db.Templates.Where(t => t.Name == req.Template && t.Status == TemplateStatuses.Approved);
        if (req.Language is not null) templates = templates.Where(t => t.Language == req.Language);
        var matches = await templates.Take(2).ToListAsync(ct);
        if (matches.Count == 0) return BadRequest(new ApiError($"No approved template named {req.Template}{(req.Language is null ? "" : $" in {req.Language}")}."));
        if (matches.Count > 1) return BadRequest(new ApiError("This template exists in several languages; pass \"language\"."));

        MediaFile? file = null;
        if (req.Document is { } doc)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(doc.ContentBase64); }
            catch (FormatException) { return BadRequest(new ApiError("document.contentBase64 is not valid base64.")); }
            (file, var error) = MediaController.FromBytes(doc.FileName, bytes);
            if (error is not null) return BadRequest(new ApiError(error));
            db.MediaFiles.Add(file!);
        }
        if (req.ScheduledAt is { } at && at < DateTime.UtcNow.AddMinutes(-1))
            return BadRequest(new ApiError("scheduledAt is in the past."));

        var keyId = int.Parse(User.FindFirst(ApiKeyHandler.KeyIdClaim)!.Value);
        var customer = await messages.GetOrCreateCustomerAsync(phone, req.Name, ct);
        var r = messages.QueueTemplate(customer, matches[0], new TemplateParams(req.Header, req.Body, req.Buttons), file,
            MessageSources.Api, apiKeyId: keyId, scheduledAt: req.ScheduledAt);
        if (r.Error is not null) return BadRequest(new ApiError(r.Error));

        await db.SaveChangesAsync(ct);
        var m = r.Message!;
        return Accepted($"/api/v1/messages/{m.Id}", ToStatus(m));
    }

    [HttpGet("messages/{id:long}")]
    public async Task<ActionResult<V1MessageStatus>> Get(long id, CancellationToken ct)
    {
        var keyId = int.Parse(User.FindFirst(ApiKeyHandler.KeyIdClaim)!.Value);
        // Each system only sees the messages it sent.
        var m = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ApiKeyId == keyId, ct);
        return m is null ? NotFound() : ToStatus(m);
    }

    private static V1MessageStatus ToStatus(Message m) =>
        new(m.Id, m.Status, m.Error, m.CreatedAt, m.SentAt, m.DeliveredAt, m.ReadAt, m.FailedAt);
}
