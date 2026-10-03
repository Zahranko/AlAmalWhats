using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

public record StatusCounts(int Queued, int Sending, int Sent, int Delivered, int Read, int Failed, int Cancelled);

public record CampaignDto(int Id, string Name, int TemplateId, string? TemplateName, string? TemplateLanguage, string Status,
    DateTime? ScheduledAt, int TotalRecipients, string? CreatedBy, DateTime CreatedAt, DateTime? CompletedAt, StatusCounts Counts);

public record CampaignRecipient([Required, MaxLength(30)] string Phone, [MaxLength(200)] string? Name,
    List<string>? Header, List<string>? Body, List<string>? Buttons, long? MediaFileId);

public record CreateCampaignRequest(
    [Required, MaxLength(200)] string Name,
    [Required] int TemplateId,
    DateTime? ScheduledAt,
    [Required, MinLength(1), MaxLength(CampaignsController.MaxRecipients)] List<CampaignRecipient> Recipients);

public record RowError(int Row, string Phone, string Error);

[ApiController]
[Route("api/campaigns")]
public class CampaignsController(AppDbContext db, MessageService messages, AuditService audit) : ControllerBase
{
    public const int MaxRecipients = 5000;

    [HttpGet]
    public async Task<PagedResult<CampaignDto>> List(int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await db.Campaigns.CountAsync(ct);
        var list = await db.Campaigns.AsNoTracking().Include(c => c.Template).Include(c => c.CreatedBy)
            .OrderByDescending(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var counts = await CountsAsync(list.Select(c => c.Id).ToList(), ct);
        return new PagedResult<CampaignDto>(list.Select(c => ToDto(c, counts)).ToList(), total, page, pageSize);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CampaignDto>> Get(int id, CancellationToken ct)
    {
        var c = await db.Campaigns.AsNoTracking().Include(x => x.Template).Include(x => x.CreatedBy).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return ToDto(c, await CountsAsync([id], ct));
    }

    /// <summary>Reads an uploaded Excel/CSV recipient list so the browser can map its columns.</summary>
    [HttpPost("parse")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public ActionResult<SheetData> Parse(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            return Csv.Read(stream, file.FileName, MaxRecipients);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or ArgumentException)
        {
            return BadRequest(new ApiError(ex is InvalidDataException ? ex.Message : $"Could not read the file: {ex.Message}"));
        }
    }

    /// <summary>
    /// Creates the campaign and queues one message per recipient, all or nothing. New phone numbers
    /// are added as customers. Opted-out recipients are reported, not queued.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CampaignDto>> Create(CreateCampaignRequest req, CancellationToken ct)
    {
        var template = await db.Templates.FindAsync([req.TemplateId], ct);
        if (template is null) return BadRequest(new ApiError("Template not found."));
        if (req.ScheduledAt is { } at && at < DateTime.UtcNow.AddMinutes(-1))
            return BadRequest(new ApiError("The scheduled time is in the past."));

        var fileIds = req.Recipients.Where(r => r.MediaFileId is not null).Select(r => r.MediaFileId!.Value).Distinct().ToList();
        var files = await db.MediaFiles.Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        // Load existing customers in one query instead of one per row.
        var phones = req.Recipients.Select(r => messages.NormalizePhone(r.Phone)).OfType<string>().Distinct().ToList();
        foreach (var chunk in phones.Chunk(1000))
            await db.Customers.Where(c => chunk.Contains(c.Phone)).LoadAsync(ct);

        var campaign = new Campaign
        {
            Name = req.Name.Trim(), TemplateId = template.Id, ScheduledAt = req.ScheduledAt,
            Status = CampaignStatuses.Scheduled, CreatedById = User.GetUserId(),
        };
        db.Campaigns.Add(campaign);

        var errors = new List<RowError>();
        var queued = 0;
        for (var i = 0; i < req.Recipients.Count; i++)
        {
            var r = req.Recipients[i];
            var phone = messages.NormalizePhone(r.Phone);
            if (phone is null) { errors.Add(new RowError(i + 1, r.Phone, "Invalid phone number.")); continue; }
            MediaFile? file = null;
            if (r.MediaFileId is { } fid && !files.TryGetValue(fid, out file))
            { errors.Add(new RowError(i + 1, r.Phone, "Attached file not found.")); continue; }

            var customer = await messages.GetOrCreateCustomerAsync(phone, r.Name, ct);
            var result = messages.QueueTemplate(customer, template, new TemplateParams(r.Header, r.Body, r.Buttons), file,
                MessageSources.Campaign, User.GetUserId(), scheduledAt: req.ScheduledAt);
            if (result.Error is not null) { errors.Add(new RowError(i + 1, r.Phone, result.Error)); continue; }
            result.Message!.Campaign = campaign;
            queued++;
        }

        // Opted-out people are expected in lists; anything else means the list needs fixing.
        var blocking = errors.Where(e => !e.Error.Contains("opted out")).ToList();
        if (blocking.Count > 0)
            return BadRequest(new { error = $"{blocking.Count} row(s) have problems. Fix them and try again.", rows = blocking.Take(100) });
        if (queued == 0) return BadRequest(new ApiError("Nobody to send to: every recipient has opted out."));

        campaign.TotalRecipients = queued;
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.CampaignCreated, $"campaign:{campaign.Id} {campaign.Name}",
            new { template = template.Name, recipients = queued, skippedOptedOut = errors.Count, req.ScheduledAt });
        await db.SaveChangesAsync(ct);
        return await Get(campaign.Id, ct);
    }

    /// <summary>Cancels messages not sent yet. Already sent messages are not affected.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var c = await db.Campaigns.FindAsync([id], ct);
        if (c is null) return NotFound();
        if (c.Status is CampaignStatuses.Completed or CampaignStatuses.Cancelled)
            return BadRequest(new ApiError($"The campaign is already {c.Status}."));

        var n = await db.Messages.Where(m => m.CampaignId == id && m.Status == MessageStatuses.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatuses.Cancelled), ct);
        c.Status = CampaignStatuses.Cancelled;
        c.CompletedAt = DateTime.UtcNow;
        audit.Record(AuditActions.CampaignCancelled, $"campaign:{c.Id} {c.Name}", new { cancelledMessages = n });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Dictionary<int, StatusCounts>> CountsAsync(List<int> ids, CancellationToken ct)
    {
        var rows = await db.Messages.Where(m => m.CampaignId != null && ids.Contains(m.CampaignId.Value))
            .GroupBy(m => new { m.CampaignId, m.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        return ids.ToDictionary(id => id, id =>
        {
            int N(string s) => rows.Where(r => r.CampaignId == id && r.Status == s).Sum(r => r.Count);
            return new StatusCounts(N(MessageStatuses.Queued), N(MessageStatuses.Sending), N(MessageStatuses.Sent),
                N(MessageStatuses.Delivered), N(MessageStatuses.Read), N(MessageStatuses.Failed), N(MessageStatuses.Cancelled));
        });
    }

    private static CampaignDto ToDto(Campaign c, Dictionary<int, StatusCounts> counts) =>
        new(c.Id, c.Name, c.TemplateId, c.Template?.Name, c.Template?.Language, c.Status, c.ScheduledAt, c.TotalRecipients,
            c.CreatedBy?.Name, c.CreatedAt, c.CompletedAt, counts.GetValueOrDefault(c.Id) ?? new StatusCounts(0, 0, 0, 0, 0, 0, 0));
}
