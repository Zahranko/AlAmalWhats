using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Controllers;

public record TemplateButtonDto(string Type, string Text);
public record TemplateDto(int Id, string Name, string Language, string Category, string Status,
    string? HeaderFormat, string? HeaderText, List<string> HeaderParams, string? BodyText, List<string> BodyParams,
    string? FooterText, List<TemplateButtonDto> Buttons, List<ButtonParam> ButtonParams, DateTime SyncedAt)
{
    public static TemplateDto From(Template t)
    {
        var s = TemplateSpec.Parse(t.ComponentsJson, t.ParameterFormat);
        return new TemplateDto(t.Id, t.Name, t.Language, t.Category, t.Status, s.HeaderFormat, s.HeaderText, s.HeaderParams,
            s.BodyText, s.BodyParams, s.FooterText, s.Buttons.Select(b => new TemplateButtonDto(b.Type, b.Text)).ToList(),
            s.ButtonParams, t.SyncedAt);
    }
}

[ApiController]
[Route("api/templates")]
public class TemplatesController(AppDbContext db, TemplateSyncService sync, AuditService audit) : ControllerBase
{
    /// <summary>All templates; ?approved=true for the ones that can be sent.</summary>
    [HttpGet]
    public async Task<List<TemplateDto>> List(bool approved = false)
    {
        var q = db.Templates.AsNoTracking().Where(t => t.Status != TemplateStatuses.Deleted);
        if (approved) q = q.Where(t => t.Status == TemplateStatuses.Approved);
        var list = await q.OrderBy(t => t.Name).ThenBy(t => t.Language).ToListAsync();
        return list.Select(TemplateDto.From).ToList();
    }

    [HttpPost("sync")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<TemplateSyncResult>> Sync(CancellationToken ct)
    {
        try
        {
            var result = await sync.SyncAsync(ct);
            audit.Record(AuditActions.TemplatesSynced, null, result);
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (WhatsAppApiException ex)
        {
            return StatusCode(502, new ApiError($"Meta: {ex.Message}"));
        }
    }
}
