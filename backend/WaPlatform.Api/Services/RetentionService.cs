using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Services;

/// <summary>
/// Deletes message data older than WhatsApp:RetentionMonths (the period promised in /privacy).
/// Runs at startup and then daily; on IIS the app may be idled, so the startup run matters.
/// </summary>
public class RetentionService(IServiceScopeFactory scopes, IOptionsMonitor<WhatsAppOptions> options, ILogger<RetentionService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMonths(-options.CurrentValue.RetentionMonths);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await db.WebhookEvents.Where(w => w.ReceivedAt < cutoff).ExecuteDeleteAsync(ct);
                if (deleted > 0) log.LogInformation("Retention: deleted {Count} webhook events older than {Cutoff:u}.", deleted, cutoff);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Retention cleanup failed; will retry on the next run.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
