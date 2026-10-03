namespace WaPlatform.Api.Services;

public static class PhoneNumbers
{
    /// <summary>
    /// Normalizes to international digits-only form: "+962 79 123 4567", "00962791234567" and
    /// "0791234567" all become "962791234567". Returns null if it can't be a valid number.
    /// </summary>
    public static string? Normalize(string? raw, string defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Arabic-Indic digits (typed on Arabic keyboards) count as digits too.
        var digits = new string(raw
            .Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c)
            .Where(char.IsAsciiDigit).ToArray());

        if (digits.StartsWith("00")) digits = digits[2..];
        else if (digits.StartsWith('0')) digits = defaultCountryCode + digits[1..];
        else if (!raw.TrimStart().StartsWith('+') && digits.Length <= 9) digits = defaultCountryCode + digits;

        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }
}
