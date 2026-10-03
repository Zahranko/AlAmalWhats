namespace WaPlatform.Api.WhatsApp;

/// <summary>
/// Meta WhatsApp Cloud API settings. Secrets (AccessToken, AppSecret, VerifyToken) never go in
/// appsettings.json: use appsettings.Local.json (git-ignored) locally and environment variables
/// in production, e.g. WhatsApp__AccessToken.
/// </summary>
public class WhatsAppOptions
{
    public string GraphApiVersion { get; set; } = "v23.0";

    /// <summary>System user (production) or temporary (testing) access token.</summary>
    public string? AccessToken { get; set; }

    /// <summary>App secret (App settings &gt; Basic); used to verify X-Hub-Signature-256 on webhooks.</summary>
    public string? AppSecret { get; set; }

    /// <summary>The value typed into "Verify token" when configuring the webhook in the Meta dashboard.</summary>
    public string? VerifyToken { get; set; }

    public string? PhoneNumberId { get; set; }
    public string? BusinessAccountId { get; set; }

    /// <summary>How long message data is kept; must match the period stated in Legal/privacy.html.</summary>
    public int RetentionMonths { get; set; } = 6;

    /// <summary>Local numbers (07...) are given this country code.</summary>
    public string DefaultCountryCode { get; set; } = "962";

    /// <summary>Send rate of the queue. Meta allows 80/s by default; stay well below it.</summary>
    public int MessagesPerSecond { get; set; } = 10;

    /// <summary>Attempts for messages that fail for temporary reasons (Meta down, rate limits).</summary>
    public int MaxAttempts { get; set; } = 4;

    public IEnumerable<string> MissingSettings()
    {
        if (string.IsNullOrWhiteSpace(AccessToken)) yield return nameof(AccessToken);
        if (string.IsNullOrWhiteSpace(AppSecret)) yield return nameof(AppSecret);
        if (string.IsNullOrWhiteSpace(VerifyToken)) yield return nameof(VerifyToken);
        if (string.IsNullOrWhiteSpace(PhoneNumberId)) yield return nameof(PhoneNumberId);
        if (string.IsNullOrWhiteSpace(BusinessAccountId)) yield return nameof(BusinessAccountId);
    }
}
