using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;

namespace WaPlatform.Api.Controllers;

public record DailyCount(DateTime Day, int Sent, int Delivered, int Read, int Failed, int Received);
public record CategoryCost(string Category, int Billable, decimal Price, decimal Cost);
public record TopTemplate(string Name, int Count);

public record DashboardDto(int Days, int Sent, int Delivered, int Read, int Failed, int Queued, int Received,
    int Customers, int OptedOut, int AwaitingReply, List<DailyCount> Daily, List<CategoryCost> Costs, decimal TotalCost,
    string Currency, List<TopTemplate> TopTemplates);

/// <summary>Price per billable message by Meta pricing category, entered by an admin from Meta's rate card.</summary>
public record PricingSettings(
    [Required, MaxLength(8)] string Currency,
    [Range(0, 100)] decimal Marketing,
    [Range(0, 100)] decimal Utility,
    [Range(0, 100)] decimal Authentication,
    [Range(0, 100)] decimal Service)
{
    public const string Key = "pricing";
    public static PricingSettings Default => new("USD", 0, 0, 0, 0);

    public decimal PriceFor(string? category) => category switch
    {
        "marketing" or "marketing_lite" => Marketing,
        "utility" => Utility,
        "authentication" or "authentication-international" => Authentication,
        "service" => Service,
        _ => 0,
    };
}

[ApiController]
[Route("api")]
public class DashboardController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<DashboardDto> Get(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var period = db.Messages.AsNoTracking().Where(m => m.CreatedAt >= since);

        var byStatus = await period.Where(m => m.Direction == MessageDirections.Out)
            .GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int S(params string[] statuses) => byStatus.Where(x => statuses.Contains(x.Key)).Sum(x => x.Count);
        var received = await period.CountAsync(m => m.Direction == MessageDirections.In, ct);

        var daily = await period
            .GroupBy(m => new { Day = m.CreatedAt.Date, m.Direction, m.Status })
            .Select(g => new { g.Key.Day, g.Key.Direction, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var series = Enumerable.Range(0, days).Select(i => since.AddDays(i)).Select(day =>
        {
            int D(Func<string, string, bool> match) => daily.Where(x => x.Day == day && match(x.Direction, x.Status)).Sum(x => x.Count);
            return new DailyCount(DateTime.SpecifyKind(day, DateTimeKind.Utc),
                D((d, s) => d == MessageDirections.Out && s is MessageStatuses.Sent or MessageStatuses.Delivered or MessageStatuses.Read),
                D((d, s) => d == MessageDirections.Out && s is MessageStatuses.Delivered or MessageStatuses.Read),
                D((d, s) => d == MessageDirections.Out && s == MessageStatuses.Read),
                D((d, s) => d == MessageDirections.Out && s == MessageStatuses.Failed),
                D((d, _) => d == MessageDirections.In));
        }).ToList();

        var pricing = await PricingAsync(db, ct);
        var billable = await period.Where(m => m.Billable == true)
            .GroupBy(m => m.PricingCategory).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var costs = billable.Select(b => new CategoryCost(b.Key ?? "unknown", b.Count, pricing.PriceFor(b.Key), b.Count * pricing.PriceFor(b.Key)))
            .OrderByDescending(c => c.Cost).ToList();

        var top = (await period.Where(m => m.TemplateName != null)
            .GroupBy(m => m.TemplateName!).Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(t => t.Count).Take(5).ToListAsync(ct))
            .Select(t => new TopTemplate(t.Name, t.Count)).ToList();

        var customers = db.Customers.AsNoTracking();
        return new DashboardDto(days,
            S(MessageStatuses.Sent, MessageStatuses.Delivered, MessageStatuses.Read),
            S(MessageStatuses.Delivered, MessageStatuses.Read),
            S(MessageStatuses.Read),
            S(MessageStatuses.Failed),
            S(MessageStatuses.Queued, MessageStatuses.Sending),
            received,
            await customers.CountAsync(ct),
            await customers.CountAsync(c => c.OptedOut, ct),
            await customers.CountAsync(c => c.LastInboundAt != null && (c.LastMessageAt == null || c.LastInboundAt > c.LastMessageAt), ct),
            series, costs, costs.Sum(c => c.Cost), pricing.Currency, top);
    }

    [HttpGet("settings/pricing")]
    [Authorize(Roles = Roles.Admin)]
    public Task<PricingSettings> GetPricing(CancellationToken ct) => PricingAsync(db, ct);

    [HttpPut("settings/pricing")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<PricingSettings> SetPricing(PricingSettings req, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(req, JsonSerializerOptions.Web);
        var row = await db.Settings.FindAsync([PricingSettings.Key], ct);
        if (row is null) db.Settings.Add(new AppSetting { Key = PricingSettings.Key, Value = json });
        else { row.Value = json; row.UpdatedAt = DateTime.UtcNow; }
        audit.Record(AuditActions.SettingsUpdated, PricingSettings.Key, req);
        await db.SaveChangesAsync(ct);
        return req;
    }

    private static async Task<PricingSettings> PricingAsync(AppDbContext db, CancellationToken ct)
    {
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == PricingSettings.Key, ct);
        return row is null ? PricingSettings.Default
            : JsonSerializer.Deserialize<PricingSettings>(row.Value, JsonSerializerOptions.Web) ?? PricingSettings.Default;
    }
}
