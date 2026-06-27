using System.Security.Cryptography;
using SouthBaySoccer.Application.Abstractions.Authentication;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Cryptographically strong refresh-token secret generator.
/// </summary>
public sealed class RefreshTokenSecretGenerator : IRefreshTokenSecretGenerator
{
    /// <inheritdoc />
    public string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
