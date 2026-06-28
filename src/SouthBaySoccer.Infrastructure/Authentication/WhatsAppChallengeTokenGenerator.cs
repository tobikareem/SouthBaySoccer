using System.Security.Cryptography;

namespace SouthBaySoccer.Infrastructure.Authentication;

public sealed class WhatsAppChallengeTokenGenerator : IWhatsAppChallengeTokenGenerator
{
    public string CreateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}