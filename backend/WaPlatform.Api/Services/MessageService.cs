using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.WhatsApp;

namespace WaPlatform.Api.Services;

public record QueueResult(Message? Message, string? Error)
{
    public static QueueResult Fail(string error) => new(null, error);
}

/// <summary>
/// Validates and queues outgoing messages. Nothing is sent here: SendWorker picks queued
/// messages up. Callers save the DbContext, so a batch (a campaign) commits all or nothing.
/// </summary>
public class MessageService(AppDbContext db, IOptionsMonitor<WhatsAppOptions> options)
{
    public string? NormalizePhone(string? raw) => PhoneNumbers.Normalize(raw, options.CurrentValue.DefaultCountryCode);

    /// <summary>Finds the customer by phone, or creates one (not saved yet) when missing.</summary>
    public async Task<Customer> GetOrCreateCustomerAsync(string phone, string? name, CancellationToken ct = default)
    {
        var customer = db.Customers.Local.FirstOrDefault(c => c.Phone == phone)
            ?? await db.Customers.FirstOrDefaultAsync(c => c.Phone == phone, ct);
        if (customer is null)
        {
            customer = new Customer { Phone = phone, Name = string.IsNullOrWhiteSpace(name) ? phone : name.Trim() };
            db.Customers.Add(customer);
        }
        return customer;
    }

    public QueueResult QueueTemplate(Customer customer, Template template, TemplateParams p, MediaFile? media,
        string source, int? userId = null, int? campaignId = null, int? apiKeyId = null, DateTime? scheduledAt = null)
    {
        if (customer.OptedOut) return QueueResult.Fail($"{customer.Phone} has opted out of WhatsApp messages.");
        if (template.Status != TemplateStatuses.Approved)
            return QueueResult.Fail($"Template {template.Name} ({template.Language}) is {template.Status.ToLowerInvariant()}, not approved.");

        var spec = TemplateSpec.Parse(template.ComponentsJson, template.ParameterFormat);
        if (spec.Validate(p, media is not null) is { } problem) return QueueResult.Fail(problem);
        if (media is not null && MediaKind(media.ContentType) is var kind && kind != spec.HeaderFormat!.ToLowerInvariant())
            return QueueResult.Fail($"This template needs a {spec.HeaderFormat!.ToLowerInvariant()}, but {media.FileName} is a {kind}.");

        var message = new Message
        {
            Customer = customer,
            Phone = customer.Phone,
            Type = "template",
            TemplateId = template.Id,
            TemplateName = template.Name,
            Language = template.Language,
            ParamsJson = p.ToJson(),
            Body = spec.Render(p),
            MediaFile = media,
            MediaFileName = media?.FileName,
            MediaContentType = media?.ContentType,
            Source = source,
            SentById = userId,
            CampaignId = campaignId,
            ApiKeyId = apiKeyId,
            ScheduledAt = scheduledAt,
        };
        db.Messages.Add(message);
        customer.LastMessageAt = DateTime.UtcNow;
        return new QueueResult(message, null);
    }

    /// <summary>A text or file reply; only allowed within 24 hours of the customer's last message.</summary>
    public QueueResult QueueReply(Customer customer, string? text, MediaFile? media, int? userId, string source = MessageSources.Reply)
    {
        if (customer.OptedOut && source != MessageSources.System)
            return QueueResult.Fail("This customer has opted out of WhatsApp messages.");
        if (!customer.CanReceiveFreeForm(DateTime.UtcNow))
            return QueueResult.Fail("The 24-hour reply window is closed. Send an approved template instead.");
        if (string.IsNullOrWhiteSpace(text) && media is null) return QueueResult.Fail("Write a message or attach a file.");
        if (text is { Length: > 4096 }) return QueueResult.Fail("Messages can be at most 4096 characters.");

        var message = new Message
        {
            Customer = customer,
            Phone = customer.Phone,
            Type = media is null ? "text" : MediaKind(media.ContentType),
            Body = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            MediaFile = media,
            MediaFileName = media?.FileName,
            MediaContentType = media?.ContentType,
            Source = source,
            SentById = userId,
        };
        db.Messages.Add(message);
        customer.LastMessageAt = DateTime.UtcNow;
        return new QueueResult(message, null);
    }

    /// <summary>WhatsApp media kind for a MIME type: image, video, audio or document.</summary>
    public static string MediaKind(string contentType) => contentType switch
    {
        "image/jpeg" or "image/png" => "image",
        "video/mp4" or "video/3gpp" => "video",
        _ when contentType.StartsWith("audio/") => "audio",
        _ => "document",
    };
}
