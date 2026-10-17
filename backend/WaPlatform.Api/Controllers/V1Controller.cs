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
    [MaxLength(50)] string? FileNumber,
    [Required, MaxLength(512)] string Template,
    [MaxLength(16)] string? Language,
    List<string>? Header,
    List<string>? Body,
    List<string>? Buttons,
    V1Document? Document,
    DateTime? ScheduledAt);

public record V1BulkRecipient(
    [MaxLength(100)] string? Ref,
    [Required, MaxLength(30)] string Phone,
    [MaxLength(200)] string? Name,
    [MaxLength(50)] string? FileNumber,
    List<string>? Header,
    List<string>? Body,
    List<string>? Buttons);

public record V1BulkRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(512)] string Template,
    [MaxLength(16)] string? Language,
    [Required, MinLength(1), MaxLength(CampaignsController.MaxRecipients)] List<V1BulkRecipient> Recipients);

/// <summary>One line per recipient, in request order: the message id, or why it wasn't queued.</summary>
public record V1BulkResult(string? Ref, string Phone, long? Id, string? Error);

public record V1BulkResponse(int? CampaignId, int Queued, int Failed, List<V1BulkResult> Results);

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
public class V1Controller(AppDbContext db, MessageService messages, AuditService audit) : ControllerBase
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

        var keyId = KeyId;
        var customer = await CustomerAsync(phone, req.Name, req.FileNumber, ct);
        var r = messages.QueueTemplate(customer, matches[0], new TemplateParams(req.Header, req.Body, req.Buttons), file,
            MessageSources.Api, apiKeyId: keyId, scheduledAt: req.ScheduledAt);
        if (r.Error is not null) return BadRequest(new ApiError(r.Error));

        await db.SaveChangesAsync(ct);
        var m = r.Message!;
        return Accepted($"/api/v1/messages/{m.Id}", ToStatus(m));
    }

    /// <summary>
    /// A bulk send: one campaign (it shows in Bulk send with the others) and one message per
    /// recipient. Unlike the console's bulk send, a bad row doesn't stop the rest: every row is
    /// answered on its own, so the calling system can mark each of its records sent or failed.
    /// Customers are created or completed exactly as for a single send.
    /// </summary>
    [HttpPost("campaigns")]
    public async Task<ActionResult<V1BulkResponse>> SendBulk(V1BulkRequest req, CancellationToken ct)
    {
        var templates = db.Templates.Where(t => t.Name == req.Template && t.Status == TemplateStatuses.Approved);
        if (req.Language is not null) templates = templates.Where(t => t.Language == req.Language);
        var matches = await templates.Take(2).ToListAsync(ct);
        if (matches.Count == 0) return BadRequest(new ApiError($"No approved template named {req.Template}{(req.Language is null ? "" : $" in {req.Language}")}."));
        if (matches.Count > 1) return BadRequest(new ApiError("This template exists in several languages; pass \"language\"."));
        var template = matches[0];

        // Load existing customers in one query instead of one per row.
        var phones = req.Recipients.Select(r => messages.NormalizePhone(r.Phone)).OfType<string>().Distinct().ToList();
        foreach (var chunk in phones.Chunk(1000))
            await db.Customers.Where(c => chunk.Contains(c.Phone)).LoadAsync(ct);

        var keyId = KeyId;
        var campaign = new Campaign { Name = req.Name.Trim(), TemplateId = template.Id, Status = CampaignStatuses.Scheduled };
        db.Campaigns.Add(campaign);

        var lines = new List<(V1BulkRecipient Recipient, Message? Message, string? Error)>();
        foreach (var r in req.Recipients)
        {
            var phone = messages.NormalizePhone(r.Phone);
            if (phone is null) { lines.Add((r, null, "Invalid phone number.")); continue; }

            var customer = await CustomerAsync(phone, r.Name, r.FileNumber, ct);
            var result = messages.QueueTemplate(customer, template, new TemplateParams(r.Header, r.Body, r.Buttons), null,
                MessageSources.Campaign, apiKeyId: keyId);
            if (result.Error is not null) { lines.Add((r, null, result.Error)); continue; }
            result.Message!.Campaign = campaign;
            lines.Add((r, result.Message, null));
        }

        var queued = lines.Count(l => l.Message is not null);
        if (queued == 0) db.Campaigns.Remove(campaign);
        else campaign.TotalRecipients = queued;
        await db.SaveChangesAsync(ct);

        if (queued > 0)
        {
            audit.Record(AuditActions.CampaignCreated, $"campaign:{campaign.Id} {campaign.Name}",
                new { template = template.Name, recipients = queued, failed = lines.Count - queued, via = User.Identity?.Name });
            await db.SaveChangesAsync(ct);
        }

        return new V1BulkResponse(queued == 0 ? null : campaign.Id, queued, lines.Count - queued,
            lines.Select(l => new V1BulkResult(l.Recipient.Ref, l.Recipient.Phone, l.Message?.Id, l.Error)).ToList());
    }

    [HttpGet("messages/{id:long}")]
    public async Task<ActionResult<V1MessageStatus>> Get(long id, CancellationToken ct)
    {
        var keyId = KeyId;
        // Each system only sees the messages it sent.
        var m = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ApiKeyId == keyId, ct);
        return m is null ? NotFound() : ToStatus(m);
    }

    private int KeyId => int.Parse(User.FindFirst(ApiKeyHandler.KeyIdClaim)!.Value);

    /// <summary>
    /// The calling system knows who this is: name a customer who was only ever a number (first
    /// seen inbound, or sent to without a name), and fill an empty file number. Names staff typed
    /// are never overwritten.
    /// </summary>
    private async Task<Customer> CustomerAsync(string phone, string? name, string? fileNumber, CancellationToken ct)
    {
        var customer = await messages.GetOrCreateCustomerAsync(phone, name, ct);
        if (!string.IsNullOrWhiteSpace(name) && customer.Name == customer.Phone)
        {
            customer.Name = name.Trim();
            customer.UpdatedAt = DateTime.UtcNow;
        }
        if (!string.IsNullOrWhiteSpace(fileNumber) && string.IsNullOrWhiteSpace(customer.FileNumber))
        {
            customer.FileNumber = fileNumber.Trim();
            customer.UpdatedAt = DateTime.UtcNow;
        }
        return customer;
    }

    private static V1MessageStatus ToStatus(Message m) =>
        new(m.Id, m.Status, m.Error, m.CreatedAt, m.SentAt, m.DeliveredAt, m.ReadAt, m.FailedAt);
}
