using System.Text.RegularExpressions;

namespace SouthBaySoccer.Services.Authentication;

public static partial class PhoneNumberValidator
{
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!AllowedCharactersRegex().IsMatch(input))
        {
            return false;
        }

        var digits = NonDigitRegex().Replace(input, string.Empty);
        if (digits.Length is < 8 or > 15)
        {
            return false;
        }

        // Players routinely type the 10-digit US form without the country code; Pickup Pal keys US
        // users by "1XXXXXXXXXX", so assume US and prepend the "1". Must stay consistent with the
        // backend's SignInByPhoneCommandValidator.NormalizeDigits rule.
        if (digits.Length == 10)
        {
            digits = $"1{digits}";
        }

        normalized = $"+{digits}";
        return true;
    }

    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigitRegex();

    [GeneratedRegex(@"^[+\d\s().-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCharactersRegex();
}
