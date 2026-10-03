namespace WaPlatform.Api.Domain;

/// <summary>
/// Raw webhook delivery from Meta (incoming messages, delivery/read statuses, template updates),
/// stored exactly as received after its signature was verified. Processing happens later
/// (phase 4 queue), so the webhook can acknowledge Meta immediately.
/// </summary>
public class WebhookEvent
{
    public long Id { get; set; }
    public required string Payload { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    /// <summary>Set when processing failed; the event is not retried automatically.</summary>
    public string? Error { get; set; }
}
