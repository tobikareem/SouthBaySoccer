using System.Security.Cryptography;
using System.Text;

namespace SouthBaySoccer.Infrastructure.Authentication;

internal static class AuthenticationHashing
{
    public static string Sha256(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    }
}
