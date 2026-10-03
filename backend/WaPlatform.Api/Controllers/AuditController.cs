using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Controllers;

public record AuditEntryDto(long Id, int? UserId, string? UserEmail, string Action, string? Target,
    string? Details, string? Ip, DateTime CreatedAt);

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

[ApiController]
[Route("api/audit")]
[Authorize(Roles = Roles.Admin)]
public class AuditController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<AuditEntryDto>> List(
        int page = 1, int pageSize = 50, string? action = null, string? search = null,
        DateTime? from = null, DateTime? to = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => a.UserEmail!.Contains(search) || a.Target!.Contains(search));
        if (from is not null) q = q.Where(a => a.CreatedAt >= from);
        if (to is not null) q = q.Where(a => a.CreatedAt < to);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditEntryDto(a.Id, a.UserId, a.UserEmail, a.Action, a.Target, a.Details, a.Ip, a.CreatedAt))
            .ToListAsync();

        return new PagedResult<AuditEntryDto>(items, total, page, pageSize);
    }

    [HttpGet("actions")]
    public Task<List<string>> Actions() =>
        db.AuditLog.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
}
