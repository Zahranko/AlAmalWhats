using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

public record InboxLastMessage(string Direction, string Type, string? TemplateName, string? Body, string? MediaFileName,
    string Status, DateTime CreatedAt);

/// <summary>A conversation in the inbox: the customer, their last message, and how many of theirs are unanswered / unread.</summary>
public record InboxItem(CustomerDto Customer, InboxLastMessage? Last, int Unanswered, int Unread);

public record InboxCounts(int All, int Unanswered, int Open, int OptedOut, int Unread);

public record InboxResult(List<InboxItem> Items, int Total, int Page, int PageSize, InboxCounts Counts);

[ApiController]
[Route("api/inbox")]
public class InboxController(AppDbContext db, MessageService messages, ChangeFeed feed, WhatsAppClient client,
    ILogger<InboxController> log) : ControllerBase
{
    /// <summary>
    /// Long poll: answers as soon as messages or conversations change (version differs from
    /// <paramref name="since"/>), or after 25 s with the same version. The browser then asks again.
    /// </summary>
    [HttpGet("changes")]
    public async Task<object> Changes(long? since, CancellationToken ct)
    {
        try { return new { version = await feed.WaitAsync(since, TimeSpan.FromSeconds(25), ct) }; }
        catch (OperationCanceledException) { return new { version = feed.Version }; }
    }

    /// <summary>
    /// Customers with at least one message, most recent conversation first.
    /// filter: all (default), unanswered (their last messages have no reply yet), open (24-hour window open), optedOut.
    /// </summary>
    [HttpGet]
    public async Task<InboxResult> List(string? search = null, string? filter = null, int page = 1, int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var windowStart = DateTime.UtcNow.AddHours(-24);

        var q = db.Customers.AsNoTracking().Where(c => c.LastInboundAt != null || c.LastMessageAt != null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var phone = messages.NormalizePhone(s);
            q = q.Where(c => c.Name.Contains(s) || c.Phone.Contains(s) || c.Phone == phone || c.FileNumber == s);
        }

        var unansweredQ = q.Where(c => c.LastInboundAt != null && (c.LastMessageAt == null || c.LastInboundAt > c.LastMessageAt));
        var openQ = q.Where(c => c.LastInboundAt > windowStart);
        var counts = new InboxCounts(await q.CountAsync(ct), await unansweredQ.CountAsync(ct), await openQ.CountAsync(ct),
            await q.CountAsync(c => c.OptedOut, ct),
            await q.CountAsync(c => c.LastInboundAt != null && (c.LastReadAt == null || c.LastInboundAt > c.LastReadAt), ct));

        q = filter switch
        {
            "unanswered" => unansweredQ,
            "open" => openQ,
            "optedOut" => q.Where(c => c.OptedOut),
            _ => q,
        };
        var total = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(c => c.LastInboundAt > c.LastMessageAt ? c.LastInboundAt : c.LastMessageAt ?? c.LastInboundAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                Customer = c,
                Last = db.Messages.Where(m => m.CustomerId == c.Id)
                    .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                    .Select(m => new InboxLastMessage(m.Direction, m.Type, m.TemplateName, m.Body, m.MediaFileName, m.Status, m.CreatedAt))
                    .FirstOrDefault(),
                Unanswered = db.Messages.Count(m => m.CustomerId == c.Id && m.Direction == MessageDirections.In
                    && (c.LastMessageAt == null || m.CreatedAt > c.LastMessageAt)),
                Unread = db.Messages.Count(m => m.CustomerId == c.Id && m.Direction == MessageDirections.In
                    && (c.LastReadAt == null || m.CreatedAt > c.LastReadAt)),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new InboxItem(CustomerDto.From(r.Customer), r.Last, r.Unanswered, r.Unread)).ToList();
        return new InboxResult(items, total, page, pageSize, counts);
    }

    /// <summary>
    /// Staff opened the conversation: clears its unread count and sends the customer read receipts
    /// (blue ticks) for everything they wrote, as WhatsApp does.
    /// </summary>
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return NotFound();
        if (customer.LastInboundAt is null || customer.LastReadAt >= customer.LastInboundAt) return NoContent();

        var since = customer.LastReadAt;
        customer.LastReadAt = customer.LastInboundAt;
        await db.SaveChangesAsync(ct);

        // Marking the newest message read marks the earlier ones too. Best effort: the inbox is read either way.
        var wamid = await db.Messages.AsNoTracking()
            .Where(m => m.CustomerId == id && m.Direction == MessageDirections.In && m.WaMessageId != null
                && (since == null || m.CreatedAt > since))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => m.WaMessageId).FirstOrDefaultAsync(ct);
        if (wamid is not null)
        {
            try { await client.MarkReadAsync(wamid, ct); }
            catch (Exception e) when (e is WhatsAppApiException or HttpRequestException)
            {
                log.LogWarning("Could not send the read receipt for {Wamid}: {Error}", wamid, e.Message);
            }
        }
        return NoContent();
    }
}
