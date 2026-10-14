namespace WaPlatform.Api.Domain;

/// <summary>
/// A browser's sign-in, kept for Auth:RefreshTokenDays after its last use. Each use replaces the
/// token with a new one in the same family; only the SHA-256 of the token is stored.
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public required string TokenHash { get; set; }
    /// <summary>Every token descended from one login shares this; signing out or a stolen token revokes them all.</summary>
    public required string FamilyId { get; set; }
    /// <summary>The user's security stamp when issued: a password change or disabling the user ends the session.</summary>
    public required string SecurityStamp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    /// <summary>When the token was exchanged for a new one; for a short while it may be exchanged again.</summary>
    public DateTime? UsedAt { get; set; }
    /// <summary>Signed out, or the session ended because a used token came back: never accepted again.</summary>
    public DateTime? RevokedAt { get; set; }
    public string? CreatedByIp { get; set; }
}
