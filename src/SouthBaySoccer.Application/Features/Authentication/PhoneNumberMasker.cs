namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Produces safe display values for phone numbers without exposing the raw number.
/// </summary>
public static class PhoneNumberMasker
{
    /// <summary>
    /// Masks a phone number while preserving only a short suffix for user confirmation.
    /// </summary>
    /// <param name="phoneNumber">The phone number to mask.</param>
    /// <returns>A masked phone value safe for client display or diagnostics.</returns>
    public static string Mask(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        var suffix = digits.Length >= 4 ? digits[^4..] : digits;

        return string.IsNullOrWhiteSpace(suffix) ? "***" : $"***-***-{suffix}";
    }
}

