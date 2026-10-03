namespace WaPlatform.Api.Domain;

/// <summary>
/// Append-only record of who did what. UserId is deliberately not a foreign key,
/// so deleting a user never deletes or rewrites their history; UserEmail keeps it readable.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserEmail { get; set; }
    public required string Action { get; set; }
    public string? Target { get; set; }
    public string? Details { get; set; }
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string LoginLocked = "auth.login_locked";
    public const string Logout = "auth.logout";
    public const string PasswordChanged = "auth.password_changed";

    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserPasswordReset = "user.password_reset";
    public const string UserUnlocked = "user.unlocked";
    public const string UserDeleted = "user.deleted";
    public const string AdminBootstrapped = "user.admin_bootstrapped";

    public const string WhatsAppTestSent = "whatsapp.test_sent";
}
