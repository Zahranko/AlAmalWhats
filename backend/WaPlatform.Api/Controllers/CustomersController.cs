using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;

namespace WaPlatform.Api.Controllers;

public record CustomerDto(int Id, string Name, string Phone, string? FileNumber, string? Notes, bool OptedOut,
    DateTime? OptedOutAt, DateTime? LastInboundAt, DateTime? LastMessageAt, bool CanReply, DateTime CreatedAt)
{
    public static CustomerDto From(Customer c) => new(c.Id, c.Name, c.Phone, c.FileNumber, c.Notes, c.OptedOut,
        c.OptedOutAt, c.LastInboundAt, c.LastMessageAt, c.CanReceiveFreeForm(DateTime.UtcNow), c.CreatedAt);
}

public record CustomerRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(30)] string Phone,
    [MaxLength(50)] string? FileNumber,
    [MaxLength(2000)] string? Notes,
    bool OptedOut = false);

public record ReplyRequest([MaxLength(4096)] string? Text, long? MediaFileId);

[ApiController]
[Route("api/customers")]
public class CustomersController(AppDbContext db, MessageService messages, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<CustomerDto>> List(string? search = null, bool? optedOut = null, bool? awaitingReply = null,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var phone = messages.NormalizePhone(s);
            q = q.Where(c => c.Name.Contains(s) || c.Phone.Contains(s) || c.Phone == phone || c.FileNumber == s);
        }
        if (optedOut is { } o) q = q.Where(c => c.OptedOut == o);
        // Customers whose last message is newer than our last message to them.
        if (awaitingReply == true) q = q.Where(c => c.LastInboundAt != null && (c.LastMessageAt == null || c.LastInboundAt > c.LastMessageAt));

        var total = await q.CountAsync(ct);
        var list = await q
            .OrderByDescending(c => c.LastInboundAt > c.LastMessageAt ? c.LastInboundAt : c.LastMessageAt ?? c.LastInboundAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<CustomerDto>(list.Select(CustomerDto.From).ToList(), total, page, pageSize);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Get(int id, CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? NotFound() : CustomerDto.From(c);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest req, CancellationToken ct)
    {
        var phone = messages.NormalizePhone(req.Phone);
        if (phone is null) return BadRequest(new ApiError("Enter a valid phone number, e.g. 0791234567 or +962791234567."));
        if (await db.Customers.AnyAsync(c => c.Phone == phone, ct))
            return Conflict(new ApiError("A customer with this phone number already exists."));

        var c = new Customer
        {
            Name = req.Name.Trim(), Phone = phone, FileNumber = Blank(req.FileNumber), Notes = Blank(req.Notes),
            OptedOut = req.OptedOut, OptedOutAt = req.OptedOut ? DateTime.UtcNow : null,
        };
        db.Customers.Add(c);
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.CustomerCreated, Target(c), new { c.Name, c.Phone, c.FileNumber });
        await db.SaveChangesAsync(ct);
        return CustomerDto.From(c);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Update(int id, CustomerRequest req, CancellationToken ct)
    {
        var c = await db.Customers.FindAsync([id], ct);
        if (c is null) return NotFound();
        var phone = messages.NormalizePhone(req.Phone);
        if (phone is null) return BadRequest(new ApiError("Enter a valid phone number, e.g. 0791234567 or +962791234567."));
        if (phone != c.Phone && await db.Customers.AnyAsync(x => x.Phone == phone && x.Id != id, ct))
            return Conflict(new ApiError("Another customer has this phone number."));

        var before = new { c.Name, c.Phone, c.FileNumber, c.OptedOut };
        c.Name = req.Name.Trim();
        c.Phone = phone;
        c.FileNumber = Blank(req.FileNumber);
        c.Notes = Blank(req.Notes);
        if (c.OptedOut != req.OptedOut)
        {
            c.OptedOut = req.OptedOut;
            c.OptedOutAt = req.OptedOut ? DateTime.UtcNow : null;
        }
        c.UpdatedAt = DateTime.UtcNow;
        audit.Record(AuditActions.CustomerUpdated, Target(c), new { before, after = new { c.Name, c.Phone, c.FileNumber, c.OptedOut } });
        await db.SaveChangesAsync(ct);
        return CustomerDto.From(c);
    }

    /// <summary>Deletes the customer; their message history is kept but no longer linked.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var c = await db.Customers.FindAsync([id], ct);
        if (c is null) return NotFound();
        await db.Messages.Where(m => m.CustomerId == id && m.Status == MessageStatuses.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatuses.Cancelled), ct);
        db.Customers.Remove(c);
        audit.Record(AuditActions.CustomerDeleted, Target(c), new { c.Name, c.Phone });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("export")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var list = await db.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        audit.Record(AuditActions.CustomersExported, null, new { rows = list.Count });
        await db.SaveChangesAsync(ct);
        var csv = Csv.Write(["name", "phone", "file_number", "notes", "opted_out", "opted_out_at", "last_inbound_at", "last_message_at", "created_at"],
            list.Select(c => new object?[] { c.Name, c.Phone, c.FileNumber, c.Notes, c.OptedOut, c.OptedOutAt, c.LastInboundAt, c.LastMessageAt, c.CreatedAt }));
        return File(csv, "text/csv; charset=utf-8", $"customers-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>The conversation, newest last. Pass ?before=messageId to load older messages.</summary>
    [HttpGet("{id:int}/messages")]
    public async Task<ActionResult<List<MessageDto>>> Conversation(int id, long? before = null, int take = 50, CancellationToken ct = default)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) return NotFound();
        var q = db.Messages.AsNoTracking().Where(m => m.CustomerId == id);
        if (before is { } b) q = q.Where(m => m.Id < b);
        var list = await q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(Math.Clamp(take, 1, 200))
            .Select(MessageDto.Projection).ToListAsync(ct);
        list.Reverse();
        return list;
    }

    /// <summary>Free-form reply (text and/or one file) within the 24-hour window.</summary>
    [HttpPost("{id:int}/reply")]
    public async Task<ActionResult<MessageDto>> Reply(int id, ReplyRequest req, CancellationToken ct)
    {
        var c = await db.Customers.FindAsync([id], ct);
        if (c is null) return NotFound();
        MediaFile? file = null;
        if (req.MediaFileId is { } fid && (file = await db.MediaFiles.FindAsync([fid], ct)) is null)
            return BadRequest(new ApiError("The attached file was not found; upload it again."));

        var r = messages.QueueReply(c, req.Text, file, User.GetUserId());
        if (r.Error is not null) return BadRequest(new ApiError(r.Error));
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.MessageReplied, Target(c), new { message = r.Message!.Id });
        await db.SaveChangesAsync(ct);
        return await db.Messages.AsNoTracking().Where(m => m.Id == r.Message.Id).Select(MessageDto.Projection).SingleAsync(ct);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Target(Customer c) => $"customer:{c.Id} {c.Phone}";
}
