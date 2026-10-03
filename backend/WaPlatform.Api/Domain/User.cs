namespace WaPlatform.Api.Domain;

public static class Roles
{
    public const string Admin = "admin";
    public const string Employee = "employee";

    public static readonly string[] All = [Admin, Employee];

    public static bool IsValid(string? role) => role is Admin or Employee;
}

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = Roles.Employee;
    public bool IsActive { get; set; } = true;

    /// <summary>Set after an admin creates the user or resets the password.</summary>
    public bool MustChangePassword { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }

    /// <summary>Changed whenever the password, role or active flag changes, which ends all existing sessions.</summary>
    public string SecurityStamp { get; set; } = NewStamp();

    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static string NewStamp() => Guid.NewGuid().ToString("N");

    public void RotateStamp()
    {
        SecurityStamp = NewStamp();
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsLockedOut(DateTime now) => LockoutUntil is { } until && until > now;
}
