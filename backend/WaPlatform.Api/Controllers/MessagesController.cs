using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

public record MessageDto(long Id, string Direction, int? CustomerId, string? CustomerName, string Phone, string Type,
    string? TemplateName, string? Language, string? Body, string? MediaFileName, bool HasMedia, string Status,
    string? Error, string Source, int? CampaignId, string? CampaignName, string? SentBy, DateTime CreatedAt,
    DateTime? ScheduledAt, DateTime? SentAt, DateTime? DeliveredAt, DateTime? ReadAt, DateTime? FailedAt)
{
    public static readonly Expression<Func<Message, MessageDto>> Projection = m => new MessageDto(
        m.Id, m.Direction, m.CustomerId, m.Customer != null ? m.Customer.Name : null, m.Phone, m.Type,
        m.TemplateName, m.Language, m.Body, m.MediaFileName, m.MediaFileId != null || m.InboundMediaId != null, m.Status,
        m.Error, m.Source, m.CampaignId, m.Campaign != null ? m.Campaign.Name : null,
        m.SentBy != null ? m.SentBy.Name : m.ApiKeyId != null ? "API" : null, m.CreatedAt,
        m.ScheduledAt, m.SentAt, m.DeliveredAt, m.ReadAt, m.FailedAt);
}

public record SendItem([Required] int TemplateId, List<string>? Header, List<string>? Body, List<string>? Buttons, List<long>? MediaFileIds);

public record SendRequest(int? CustomerId, string? Phone, [MaxLength(200)] string? Name,
    [Required, MinLength(1), MaxLength(20)] List<SendItem> Items, DateTime? ScheduledAt);

public record MessageFilter(string? Search, string? Status, string? Direction, int? TemplateId, int? CampaignId,
    int? CustomerId, string? Source, DateTime? From, DateTime? To);

[ApiController]
[Route("api/messages")]
public class MessagesController(AppDbContext db, MessageService messages, AuditService audit, WhatsAppClient client) : ControllerBase
{
    /// <summary>
    /// Sends one or more templates to one person. An item with several files sends the template
    /// once per file (e.g. three lab reports with a document header).
    /// </summary>
    [HttpPost("send")]
    public async Task<ActionResult<List<MessageDto>>> Send(SendRequest req, CancellationToken ct)
    {
        Customer? customer;
        if (req.CustomerId is { } cid)
        {
            customer = await db.Customers.FindAsync([cid], ct);
            if (customer is null) return NotFound(new ApiError("Customer not found."));
        }
        else
        {
            var phone = messages.NormalizePhone(req.Phone);
            if (phone is null) return BadRequest(new ApiError("Enter a valid phone number."));
            customer = await messages.GetOrCreateCustomerAsync(phone, req.Name, ct);
        }
        if (req.ScheduledAt is { } at && at < DateTime.UtcNow.AddMinutes(-1))
            return BadRequest(new ApiError("The scheduled time is in the past."));

        var templateIds = req.Items.Select(i => i.TemplateId).Distinct().ToList();
        var templates = await db.Templates.Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var fileIds = req.Items.SelectMany(i => i.MediaFileIds ?? []).Distinct().ToList();
        var files = await db.MediaFiles.Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        var queued = new List<Message>();
        foreach (var item in req.Items)
        {
            if (!templates.TryGetValue(item.TemplateId, out var template)) return BadRequest(new ApiError("Template not found."));
            var p = new TemplateParams(item.Header, item.Body, item.Buttons);
            var itemFiles = (item.MediaFileIds ?? []).Select(id => files.GetValueOrDefault(id)).ToList();
            if (itemFiles.Any(f => f is null)) return BadRequest(new ApiError("An attached file was not found; upload it again."));

            foreach (var file in itemFiles.Count == 0 ? [null] : itemFiles)
            {
                var r = messages.QueueTemplate(customer, template, p, file, MessageSources.Manual, User.GetUserId(), scheduledAt: req.ScheduledAt);
                if (r.Error is not null) return BadRequest(new ApiError($"{template.Name}: {r.Error}"));
                queued.Add(r.Message!);
            }
        }

        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.MessageSent, $"customer:{customer.Id} {customer.Phone}",
            new { messages = queued.Select(m => m.Id), templates = queued.Select(m => m.TemplateName).Distinct(), req.ScheduledAt });
        await db.SaveChangesAsync(ct);

        var ids = queued.Select(m => m.Id).ToList();
        return await db.Messages.AsNoTracking().Where(m => ids.Contains(m.Id)).OrderBy(m => m.Id).Select(MessageDto.Projection).ToListAsync(ct);
    }

    [HttpGet]
    public async Task<PagedResult<MessageDto>> List([FromQuery] MessageFilter f, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = Filter(f);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(MessageDto.Projection).ToListAsync(ct);
        return new PagedResult<MessageDto>(items, total, page, pageSize);
    }

    [HttpGet("export")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Export([FromQuery] MessageFilter f, CancellationToken ct)
    {
        var rows = await Filter(f).OrderByDescending(m => m.Id).Take(100_000).Select(MessageDto.Projection).ToListAsync(ct);
        audit.Record(AuditActions.MessagesExported, null, new { f, rows = rows.Count });
        await db.SaveChangesAsync(ct);

        var csv = Csv.Write(
            ["id", "created_at", "direction", "customer", "phone", "type", "template", "language", "text", "file", "status",
             "error", "source", "campaign", "sent_by", "scheduled_at", "sent_at", "delivered_at", "read_at", "failed_at"],
            rows.Select(m => new object?[] { m.Id, m.CreatedAt, m.Direction, m.CustomerName, m.Phone, m.Type, m.TemplateName,
                m.Language, m.Body, m.MediaFileName, m.Status, m.Error, m.Source, m.CampaignName, m.SentBy,
                m.ScheduledAt, m.SentAt, m.DeliveredAt, m.ReadAt, m.FailedAt }));
        return File(csv, "text/csv; charset=utf-8", $"messages-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>The attachment of a message: our uploaded file, or media a customer sent (fetched from Meta).</summary>
    [HttpGet("{id:long}/media")]
    public async Task<IActionResult> Media(long id, CancellationToken ct)
    {
        var m = await db.Messages.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.MediaFileId, x.InboundMediaId, x.MediaFileName, x.MediaContentType, x.Type }).SingleOrDefaultAsync(ct);
        if (m is null) return NotFound();

        if (m.MediaFileId is { } fileId)
        {
            var file = await db.MediaFiles.FindAsync([fileId], ct);
            return file is null ? NotFound(new ApiError("The file was deleted.")) : File(file.Content, file.ContentType, file.FileName);
        }
        if (m.InboundMediaId is null) return NotFound();
        try
        {
            var (content, type) = await client.DownloadMediaAsync(m.InboundMediaId, ct);
            var name = m.MediaFileName ?? $"{m.Type}-{id}{Extension(type)}";
            return File(content, type, name);
        }
        catch (WhatsAppApiException ex)
        {
            return StatusCode(502, new ApiError($"Could not download from WhatsApp (media is kept for 30 days): {ex.Message}"));
        }
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var n = await db.Messages.Where(m => m.Id == id && m.Status == MessageStatuses.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatuses.Cancelled), ct);
        if (n == 0) return BadRequest(new ApiError("Only messages that are still queued can be cancelled."));
        audit.Record(AuditActions.MessageCancelled, $"message:{id}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Queues a copy of a failed outgoing message.</summary>
    [HttpPost("{id:long}/resend")]
    public async Task<ActionResult<MessageDto>> Resend(long id, CancellationToken ct)
    {
        var m = await db.Messages.Include(x => x.Customer).Include(x => x.Template).Include(x => x.MediaFile)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        if (m.Direction != MessageDirections.Out || m.Status != MessageStatuses.Failed)
            return BadRequest(new ApiError("Only failed outgoing messages can be resent."));
        if (m.Customer is null) return BadRequest(new ApiError("The customer was deleted."));
        if (m.MediaFileName is not null && m.MediaFile is null) return BadRequest(new ApiError("The attachment was deleted."));

        QueueResult r = m.Type == "template"
            ? m.Template is null
                ? QueueResult.Fail("The template no longer exists.")
                : messages.QueueTemplate(m.Customer, m.Template, TemplateParams.FromJson(m.ParamsJson), m.MediaFile,
                    m.Source == MessageSources.Campaign ? MessageSources.Manual : m.Source, User.GetUserId())
            : messages.QueueReply(m.Customer, m.Body, m.MediaFile, User.GetUserId());
        if (r.Error is not null) return BadRequest(new ApiError(r.Error));

        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.MessageSent, $"customer:{m.Customer.Id} {m.Customer.Phone}", new { resendOf = id, message = r.Message!.Id });
        await db.SaveChangesAsync(ct);
        return await db.Messages.AsNoTracking().Where(x => x.Id == r.Message!.Id).Select(MessageDto.Projection).SingleAsync(ct);
    }

    private IQueryable<Message> Filter(MessageFilter f)
    {
        var q = db.Messages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(m => m.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Direction)) q = q.Where(m => m.Direction == f.Direction);
        if (!string.IsNullOrWhiteSpace(f.Source)) q = q.Where(m => m.Source == f.Source);
        if (f.TemplateId is { } t) q = q.Where(m => m.TemplateId == t);
        if (f.CampaignId is { } c) q = q.Where(m => m.CampaignId == c);
        if (f.CustomerId is { } cu) q = q.Where(m => m.CustomerId == cu);
        if (f.From is { } from) q = q.Where(m => m.CreatedAt >= from);
        if (f.To is { } to) q = q.Where(m => m.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            var phone = messages.NormalizePhone(s);
            q = q.Where(m => m.Phone == phone || m.Phone.Contains(s) || m.Customer!.Name.Contains(s)
                || m.Customer.FileNumber == s || m.TemplateName!.Contains(s));
        }
        return q;
    }

    private static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "video/mp4" => ".mp4",
        "audio/ogg" or "audio/ogg; codecs=opus" => ".ogg",
        "audio/mpeg" => ".mp3",
        "application/pdf" => ".pdf",
        _ => "",
    };
}
