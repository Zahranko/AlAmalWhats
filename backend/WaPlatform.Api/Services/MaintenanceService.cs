using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Services;

/// <summary>
/// Hourly housekeeping, also run at startup (on IIS the app may have been idle):
/// - syncs templates from Meta, so new and changed templates appear without a manual sync;
/// - deletes message data older than WhatsApp:RetentionMonths, the period promised in /privacy.
/// </summary>
public class MaintenanceService(IServiceScopeFactory scopes, IOptionsMonitor<WhatsAppOptions> options, ILogger<MaintenanceService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            await RunAsync("Template sync", SyncTemplatesAsync, ct);
            await RunAsync("Retention cleanup", ApplyRetentionAsync, ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunAsync(string name, Func<IServiceProvider, CancellationToken, Task> job, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await job(scope.ServiceProvider, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "{Job} failed; will retry next hour.", name);
        }
    }

    private async Task SyncTemplatesAsync(IServiceProvider sp, CancellationToken ct)
    {
        if (options.CurrentValue.MissingSettings().Any()) return;
        var r = await sp.GetRequiredService<TemplateSyncService>().SyncAsync(ct);
        if (r.Added + r.Updated + r.Removed > 0)
            log.LogInformation("Templates synced: {Added} added, {Updated} updated, {Removed} removed.", r.Added, r.Updated, r.Removed);
    }

    private async Task ApplyRetentionAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.AddMonths(-options.CurrentValue.RetentionMonths);

        var events = await db.WebhookEvents.Where(w => w.ReceivedAt < cutoff).ExecuteDeleteAsync(ct);
        var messages = await db.Messages
            .Where(m => m.CreatedAt < cutoff && m.Status != MessageStatuses.Queued && m.Status != MessageStatuses.Sending)
            .ExecuteDeleteAsync(ct);
        // Files are deleted with their messages; uploads never attached to a message go after 2 days.
        var orphanCutoff = DateTime.UtcNow.AddDays(-2);
        var files = await db.MediaFiles
            .Where(f => f.CreatedAt < orphanCutoff && !db.Messages.Any(m => m.MediaFileId == f.Id))
            .ExecuteDeleteAsync(ct);
        var campaigns = await db.Campaigns
            .Where(c => c.CreatedAt < cutoff && !db.Messages.Any(m => m.CampaignId == c.Id))
            .ExecuteDeleteAsync(ct);

        if (events + messages + files + campaigns > 0)
            log.LogInformation("Retention: deleted {Messages} messages, {Files} files, {Campaigns} campaigns, {Events} webhook events older than {Cutoff:u}.",
                messages, files, campaigns, events, cutoff);
    }
}
