using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Services;

/// <summary>
/// Sends queued messages at WhatsApp:MessagesPerSecond. Each message is claimed with an atomic
/// queued→sending update, so even two app instances (IIS overlapped recycle) never send twice.
/// </summary>
public class SendWorker(IServiceScopeFactory scopes, IOptionsMonitor<WhatsAppOptions> options, ILogger<SendWorker> log)
    : BackgroundService
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverInterruptedAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                await UpdateCampaignsAsync(ct);
                sent = await SendDueAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Send loop failed; retrying shortly.");
            }
            if (sent == 0) await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    /// <summary>
    /// A message left in "sending" by a crash may or may not have reached Meta. Mark it failed
    /// rather than resend, so a patient never gets the same report twice.
    /// </summary>
    private async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var n = await db.Messages
            .Where(m => m.Status == MessageStatuses.Sending && (m.NextAttemptAt ?? m.CreatedAt) < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatuses.Failed)
                .SetProperty(m => m.FailedAt, DateTime.UtcNow)
                .SetProperty(m => m.Error, "Interrupted while sending; it may or may not have been delivered. Check before resending."), ct);
        if (n > 0) log.LogWarning("Marked {Count} interrupted messages as failed.", n);
    }

    private async Task UpdateCampaignsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        await db.Campaigns
            .Where(c => c.Status == CampaignStatuses.Scheduled && (c.ScheduledAt == null || c.ScheduledAt <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatuses.Sending), ct);

        await db.Campaigns
            .Where(c => c.Status == CampaignStatuses.Sending
                && !db.Messages.Any(m => m.CampaignId == c.Id && (m.Status == MessageStatuses.Queued || m.Status == MessageStatuses.Sending)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CampaignStatuses.Completed)
                .SetProperty(c => c.CompletedAt, now), ct);
    }

    private async Task<int> SendDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<WhatsAppClient>();
        var now = DateTime.UtcNow;

        var ids = await db.Messages
            .Where(m => m.Status == MessageStatuses.Queued
                && (m.ScheduledAt == null || m.ScheduledAt <= now)
                && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.Id).Select(m => m.Id).Take(25).ToListAsync(ct);

        var sent = 0;
        foreach (var id in ids)
        {
            var claimed = await db.Messages
                .Where(m => m.Id == id && m.Status == MessageStatuses.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, MessageStatuses.Sending)
                    .SetProperty(m => m.NextAttemptAt, DateTime.UtcNow), ct);
            if (claimed == 0) continue;

            var message = await db.Messages
                .Include(m => m.Customer).Include(m => m.Template).Include(m => m.MediaFile)
                .SingleAsync(m => m.Id == id, ct);
            await SendOneAsync(db, client, message, ct);
            db.ChangeTracker.Clear();
            sent++;

            await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, options.CurrentValue.MessagesPerSecond)), ct);
        }
        return sent;
    }

    private async Task SendOneAsync(AppDbContext db, WhatsAppClient client, Message m, CancellationToken ct)
    {
        m.Attempts++;
        try
        {
            if (m.Customer is { OptedOut: true } && m.Source != MessageSources.System)
                throw new WhatsAppApiException("The customer opted out before this message was sent.", 400, null);

            var (type, content) = await BuildAsync(db, client, m, ct);
            m.WaMessageId = await client.SendMessageAsync(m.Phone, type, content, ct);
            m.Status = MessageStatuses.Sent;
            m.SentAt = DateTime.UtcNow;
            m.Error = null;
            m.ErrorCode = null;
        }
        catch (WhatsAppApiException ex) when (ex.IsTransient && m.Attempts < options.CurrentValue.MaxAttempts)
        {
            m.Status = MessageStatuses.Queued;
            m.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(m.Attempts - 1, Backoff.Length - 1)];
            m.Error = $"Attempt {m.Attempts} failed, retrying: {ex.Message}";
            m.ErrorCode = ex.MetaCode;
            log.LogWarning("Message {Id} attempt {Attempt} failed, will retry: {Error}", m.Id, m.Attempts, ex.Message);
        }
        catch (Exception ex) when (ex is WhatsAppApiException or InvalidOperationException)
        {
            m.Status = MessageStatuses.Failed;
            m.FailedAt = DateTime.UtcNow;
            m.Error = Truncate(ex.Message, 1000);
            m.ErrorCode = (ex as WhatsAppApiException)?.MetaCode;
            log.LogWarning("Message {Id} failed: {Error}", m.Id, ex.Message);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<(string Type, JsonObject Content)> BuildAsync(AppDbContext db, WhatsAppClient client, Message m, CancellationToken ct)
    {
        string? mediaId = null;
        if (m.MediaFile is { } file)
        {
            // Meta deletes uploads after 30 days; re-upload a little before that.
            if (file.MetaMediaId is null || file.MetaUploadedAt < DateTime.UtcNow.AddDays(-25))
            {
                file.MetaMediaId = await client.UploadMediaAsync(file.Content, file.ContentType, file.FileName, ct);
                file.MetaUploadedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            mediaId = file.MetaMediaId;
        }

        switch (m.Type)
        {
            case "template":
                var template = m.Template ?? throw new InvalidOperationException("The template was deleted before sending.");
                var spec = TemplateSpec.Parse(template.ComponentsJson, template.ParameterFormat);
                var content = new JsonObject
                {
                    ["name"] = m.TemplateName,
                    ["language"] = new JsonObject { ["code"] = m.Language },
                };
                var components = spec.BuildComponents(TemplateParams.FromJson(m.ParamsJson), mediaId, m.MediaFileName);
                if (components.Count > 0) content["components"] = components;
                return ("template", content);

            case "text":
                return ("text", new JsonObject { ["body"] = m.Body, ["preview_url"] = false });

            case "image" or "video" or "document" or "audio":
                var media = new JsonObject { ["id"] = mediaId ?? throw new InvalidOperationException("The attachment was deleted before sending.") };
                if (m.Type != "audio" && !string.IsNullOrEmpty(m.Body)) media["caption"] = m.Body;
                if (m.Type == "document") media["filename"] = m.MediaFileName;
                return (m.Type, media);

            default:
                throw new InvalidOperationException($"Cannot send messages of type {m.Type}.");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
