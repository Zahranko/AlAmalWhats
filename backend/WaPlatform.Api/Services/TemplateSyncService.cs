using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Services;

public record TemplateSyncResult(int Added, int Updated, int Removed);

/// <summary>Mirrors the templates in WhatsApp Manager into the templates table.</summary>
public class TemplateSyncService(AppDbContext db, WhatsAppClient client)
{
    public async Task<TemplateSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var remote = await client.GetTemplatesAsync(ct);
        var local = await db.Templates.ToDictionaryAsync(t => t.MetaId, ct);
        int added = 0, updated = 0, removed = 0;
        var now = DateTime.UtcNow;

        foreach (var r in remote)
        {
            if (!local.Remove(r.Id, out var t))
            {
                t = new Template { MetaId = r.Id, Name = r.Name, Language = r.Language };
                db.Templates.Add(t);
                added++;
            }
            else if (t.Status != r.Status || t.ComponentsJson != r.ComponentsJson || t.Category != r.Category)
                updated++;

            t.Name = r.Name;
            t.Language = r.Language;
            t.Category = r.Category;
            t.Status = r.Status;
            t.ParameterFormat = r.ParameterFormat;
            t.ComponentsJson = r.ComponentsJson;
            t.SyncedAt = now;
        }

        // Deleted in WhatsApp Manager: keep the row (history and campaigns point to it), mark it.
        foreach (var gone in local.Values.Where(t => t.Status != TemplateStatuses.Deleted))
        {
            gone.Status = TemplateStatuses.Deleted;
            gone.SyncedAt = now;
            removed++;
        }

        await db.SaveChangesAsync(ct);
        return new TemplateSyncResult(added, updated, removed);
    }
}
