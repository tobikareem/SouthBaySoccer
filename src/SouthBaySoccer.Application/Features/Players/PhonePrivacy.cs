using System.Security.Cryptography;
using System.Text;

namespace SouthBaySoccer.Application.Features.Players;

internal static class PhonePrivacy
{
    public static string Hash(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phoneNumber.Trim())));
    }

    public static string Mask(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4)
        {
            return "***";
        }

        return $"{phoneNumber.Trim()[0]}******{digits[^4..]}";
    }
}
