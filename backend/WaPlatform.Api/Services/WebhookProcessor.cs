using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Services;

/// <summary>
/// Applies stored webhook events: delivery statuses, incoming messages (including STOP/START
/// opt-out keywords) and template status changes. Each step is idempotent, because Meta can
/// deliver the same event more than once.
/// </summary>
public class WebhookProcessor(IServiceScopeFactory scopes, ILogger<WebhookProcessor> log) : BackgroundService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "stop", "unsubscribe", "إيقاف", "ايقاف", "توقف", "الغاء", "إلغاء", "الغاء الاشتراك", "إلغاء الاشتراك" };
    private static readonly HashSet<string> StartWords = new(StringComparer.OrdinalIgnoreCase)
        { "start", "subscribe", "اشتراك", "تفعيل" };

    private const string StopReply = "You will no longer receive WhatsApp messages from AlAmal Hospital. Reply START to subscribe again.\n" +
        "لن تصلك رسائل واتساب من مستشفى الأمل بعد الآن. أرسل START للاشتراك مجدداً.";
    private const string StartReply = "You are subscribed again to WhatsApp messages from AlAmal Hospital.\n" +
        "تم تفعيل اشتراكك في رسائل واتساب من مستشفى الأمل.";

    /// <summary>A status can arrive before the send that produced it is saved; wait this long for it.</summary>
    private static readonly TimeSpan UnknownMessageGrace = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var processed = 0;
            try { processed = await ProcessBatchAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Webhook processing loop failed."); }
            if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messages = scope.ServiceProvider.GetRequiredService<MessageService>();

        // Untracked: each event's changes are saved (or discarded) on their own below.
        var events = await db.WebhookEvents.AsNoTracking().Where(e => e.ProcessedAt == null).OrderBy(e => e.Id).Take(50).ToListAsync(ct);
        var done = 0;
        foreach (var e in events)
        {
            try
            {
                var complete = await ApplyAsync(db, messages, JsonNode.Parse(e.Payload), e.ReceivedAt, ct);
                if (!complete) { db.ChangeTracker.Clear(); continue; } // try again on a later pass

                // The event's effects and its "processed" mark commit together.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.SaveChangesAsync(ct);
                await db.WebhookEvents.Where(w => w.Id == e.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.ProcessedAt, DateTime.UtcNow), ct);
                await tx.CommitAsync(ct);
                db.ChangeTracker.Clear();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Webhook event {Id} could not be processed.", e.Id);
                db.ChangeTracker.Clear();
                await db.WebhookEvents.Where(w => w.Id == e.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.ProcessedAt, DateTime.UtcNow)
                    .SetProperty(w => w.Error, ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message), ct);
            }
            done++;
        }
        return done;
    }

    /// <summary>Returns false when the event should be retried later.</summary>
    private async Task<bool> ApplyAsync(AppDbContext db, MessageService messages, JsonNode? payload, DateTime receivedAt, CancellationToken ct)
    {
        foreach (var entry in payload?["entry"]?.AsArray() ?? [])
        foreach (var change in entry?["changes"]?.AsArray() ?? [])
        {
            var field = change?["field"]?.GetValue<string>();
            var value = change?["value"];
            if (value is null) continue;

            if (field == "messages")
            {
                foreach (var status in value["statuses"]?.AsArray() ?? [])
                    if (!await ApplyStatusAsync(db, status!, receivedAt, ct)) return false;

                var contacts = value["contacts"]?.AsArray();
                foreach (var msg in value["messages"]?.AsArray() ?? [])
                    await ApplyInboundAsync(db, messages, msg!, contacts, ct);
            }
            else if (field == "message_template_status_update")
            {
                var name = value["message_template_name"]?.GetValue<string>();
                var lang = value["message_template_language"]?.GetValue<string>();
                var status = value["event"]?.GetValue<string>();
                if (name is not null && status is not null)
                    await db.Templates.Where(t => t.Name == name && t.Language == lang)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status), ct);
            }
        }
        return true;
    }

    private static async Task<bool> ApplyStatusAsync(AppDbContext db, JsonNode s, DateTime receivedAt, CancellationToken ct)
    {
        var wamid = s["id"]?.GetValue<string>();
        var status = s["status"]?.GetValue<string>();
        if (wamid is null || status is null) return true;

        var message = await db.Messages.FirstOrDefaultAsync(m => m.WaMessageId == wamid, ct);
        if (message is null)
            // Sent outside this platform, or our send hasn't been saved yet.
            return DateTime.UtcNow - receivedAt > UnknownMessageGrace;

        var at = long.TryParse(s["timestamp"]?.GetValue<string>(), out var ts) ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : DateTime.UtcNow;
        switch (status)
        {
            case MessageStatuses.Sent: message.SentAt ??= at; break;
            case MessageStatuses.Delivered: message.DeliveredAt ??= at; message.SentAt ??= at; break;
            case MessageStatuses.Read: message.ReadAt ??= at; message.DeliveredAt ??= at; message.SentAt ??= at; break;
            case MessageStatuses.Failed:
                message.FailedAt = at;
                var err = s["errors"]?[0];
                message.ErrorCode = err?["code"]?.GetValue<int>();
                var detail = err?["error_data"]?["details"]?.GetValue<string>() ?? err?["message"]?.GetValue<string>() ?? err?["title"]?.GetValue<string>();
                message.Error = $"({message.ErrorCode}) {detail}";
                if (message.Error.Length > 1000) message.Error = message.Error[..1000];
                break;
        }

        // Statuses can arrive out of order: never move back, but a failure always wins over "sent".
        if (status == MessageStatuses.Failed
                ? message.Status is not (MessageStatuses.Delivered or MessageStatuses.Read)
                : MessageStatuses.Rank(status) > MessageStatuses.Rank(message.Status) && message.Status != MessageStatuses.Failed)
            message.Status = status;

        if (s["pricing"] is { } pricing)
        {
            message.PricingCategory = pricing["category"]?.GetValue<string>();
            message.Billable = pricing["billable"]?.GetValue<bool>();
        }
        return true;
    }

    private async Task ApplyInboundAsync(AppDbContext db, MessageService messages, JsonNode m, JsonArray? contacts, CancellationToken ct)
    {
        var wamid = m["id"]?.GetValue<string>();
        var from = m["from"]?.GetValue<string>();
        if (wamid is null || from is null) return;
        if (await db.Messages.AnyAsync(x => x.WaMessageId == wamid, ct)) return; // duplicate delivery

        var profileName = contacts?.FirstOrDefault(c => c?["wa_id"]?.GetValue<string>() == from)?["profile"]?["name"]?.GetValue<string>();
        var customer = await messages.GetOrCreateCustomerAsync(from, profileName, ct);
        var at = long.TryParse(m["timestamp"]?.GetValue<string>(), out var ts) ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : DateTime.UtcNow;
        customer.LastInboundAt = customer.LastInboundAt is { } prev && prev > at ? prev : at;

        var type = m["type"]?.GetValue<string>() ?? "unknown";
        var media = type is "image" or "video" or "audio" or "document" or "sticker" ? m[type] : null;
        var message = new Message
        {
            Direction = MessageDirections.In,
            Customer = customer,
            Phone = from,
            Type = type,
            Body = InboundText(m, type),
            InboundMediaId = media?["id"]?.GetValue<string>(),
            MediaFileName = media?["filename"]?.GetValue<string>(),
            MediaContentType = media?["mime_type"]?.GetValue<string>(),
            Status = MessageStatuses.Received,
            WaMessageId = wamid,
            Source = MessageSources.Manual,
            CreatedAt = at,
        };
        db.Messages.Add(message);

        var keyword = message.Body?.Trim().TrimEnd('.', '!');
        if (keyword is not null && StopWords.Contains(keyword) && !customer.OptedOut)
        {
            customer.OptedOut = true;
            customer.OptedOutAt = DateTime.UtcNow;
            messages.QueueReply(customer, StopReply, null, null, MessageSources.System);
            log.LogInformation("Customer {Phone} opted out.", from);
        }
        else if (keyword is not null && StartWords.Contains(keyword) && customer.OptedOut)
        {
            customer.OptedOut = false;
            customer.OptedOutAt = null;
            messages.QueueReply(customer, StartReply, null, null, MessageSources.System);
            log.LogInformation("Customer {Phone} opted back in.", from);
        }
        customer.UpdatedAt = DateTime.UtcNow;
    }

    private static string? InboundText(JsonNode m, string type) => type switch
    {
        "text" => m["text"]?["body"]?.GetValue<string>(),
        "image" or "video" or "document" => m[type]?["caption"]?.GetValue<string>(),
        "button" => m["button"]?["text"]?.GetValue<string>(),
        "interactive" => m["interactive"]?["button_reply"]?["title"]?.GetValue<string>()
            ?? m["interactive"]?["list_reply"]?["title"]?.GetValue<string>(),
        "reaction" => m["reaction"]?["emoji"]?.GetValue<string>(),
        "location" => $"📍 {m["location"]?["latitude"]}, {m["location"]?["longitude"]} {m["location"]?["name"]}".Trim(),
        "contacts" => "Shared a contact",
        _ => null,
    };
}
