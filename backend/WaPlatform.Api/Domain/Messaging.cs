namespace WaPlatform.Api.Domain;

/// <summary>A message template as approved in WhatsApp Manager, mirrored by template sync.</summary>
public class Template
{
    public int Id { get; set; }
    public required string MetaId { get; set; }
    public required string Name { get; set; }
    public required string Language { get; set; }
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
    /// <summary>POSITIONAL ({{1}}) or NAMED ({{patient_name}}).</summary>
    public string ParameterFormat { get; set; } = "POSITIONAL";
    /// <summary>Meta's "components" array, kept as received.</summary>
    public string ComponentsJson { get; set; } = "[]";
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public static class TemplateStatuses
{
    public const string Approved = "APPROVED";
    public const string Deleted = "DELETED";
}

public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>International format, digits only (962791234567).</summary>
    public required string Phone { get; set; }
    /// <summary>Hospital file / medical record number, optional.</summary>
    public string? FileNumber { get; set; }
    public string? Notes { get; set; }
    public bool OptedOut { get; set; }
    public DateTime? OptedOutAt { get; set; }
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Free-form messages are only allowed within 24 hours of the customer's last message.</summary>
    public bool CanReceiveFreeForm(DateTime now) => LastInboundAt is { } t && now - t < TimeSpan.FromHours(24);
}

/// <summary>A file uploaded by staff or the API, sent as a template header or a reply attachment.</summary>
public class MediaFile
{
    public long Id { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public required byte[] Content { get; set; }
    /// <summary>Meta media id from the last upload; Meta keeps uploads for 30 days.</summary>
    public string? MetaMediaId { get; set; }
    public DateTime? MetaUploadedAt { get; set; }
    public int? UploadedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Message
{
    public long Id { get; set; }
    public string Direction { get; set; } = MessageDirections.Out;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public required string Phone { get; set; }

    /// <summary>template, text, image, document, video, audio, location, ... (Meta's message types).</summary>
    public required string Type { get; set; }
    public int? TemplateId { get; set; }
    public Template? Template { get; set; }
    public string? TemplateName { get; set; }
    public string? Language { get; set; }
    /// <summary>Template variables: {"header":[...],"body":[...],"buttons":[...]}.</summary>
    public string? ParamsJson { get; set; }
    /// <summary>Text shown in history: the rendered template body, the text sent, or the text received.</summary>
    public string? Body { get; set; }

    public long? MediaFileId { get; set; }
    public MediaFile? MediaFile { get; set; }
    /// <summary>Incoming media: Meta's media id (downloaded on demand) and its file name/type.</summary>
    public string? InboundMediaId { get; set; }
    public string? MediaFileName { get; set; }
    public string? MediaContentType { get; set; }

    public string Status { get; set; } = MessageStatuses.Queued;
    public string? Error { get; set; }
    public int? ErrorCode { get; set; }
    public string? WaMessageId { get; set; }

    public string Source { get; set; } = MessageSources.Manual;
    public int? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public int? SentById { get; set; }
    public User? SentBy { get; set; }
    public int? ApiKeyId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }

    /// <summary>From Meta's pricing info on status webhooks; used for the cost estimate.</summary>
    public string? PricingCategory { get; set; }
    public bool? Billable { get; set; }
}

public static class MessageDirections
{
    public const string Out = "out";
    public const string In = "in";
}

public static class MessageStatuses
{
    public const string Queued = "queued";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Received = "received";

    /// <summary>Webhooks can arrive out of order; a status only replaces a lower-ranked one.</summary>
    public static int Rank(string status) => status switch
    {
        Queued => 0,
        Sending => 1,
        Sent => 2,
        Delivered => 3,
        Read => 4,
        _ => 5,
    };
}

public static class MessageSources
{
    public const string Manual = "manual";
    public const string Reply = "reply";
    public const string Campaign = "campaign";
    public const string Api = "api";
    public const string System = "system";
}

public class Campaign
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int TemplateId { get; set; }
    public Template? Template { get; set; }
    public string Status { get; set; } = CampaignStatuses.Scheduled;
    public DateTime? ScheduledAt { get; set; }
    public int TotalRecipients { get; set; }
    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public static class CampaignStatuses
{
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>Key for the hospital's other systems to send messages through /api/v1. Only a hash is stored.</summary>
public class ApiKey
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>First characters of the key, shown so admins can tell keys apart.</summary>
    public required string Prefix { get; set; }
    public required string KeyHash { get; set; }
    public int? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>Small admin-editable settings (JSON values), e.g. message prices.</summary>
public class AppSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
