namespace WaPlatform.Api.Auth;

public static class Passwords
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
    private const int WorkFactor = 12;

    // Verified against when the email is unknown, so response time doesn't reveal which emails exist.
    private static readonly Lazy<string> DummyHash = new(() => Hash(Guid.NewGuid().ToString()));

    // Enhanced = SHA-384 pre-hash, which avoids bcrypt's 72-byte input truncation.
    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);

    public static bool Verify(string password, string? hash) =>
        BCrypt.Net.BCrypt.EnhancedVerify(password, hash ?? DummyHash.Value);

    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (password.Length > MaxLength)
            return $"Password must be at most {MaxLength} characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Password must contain letters and numbers.";
        return null;
    }
}
