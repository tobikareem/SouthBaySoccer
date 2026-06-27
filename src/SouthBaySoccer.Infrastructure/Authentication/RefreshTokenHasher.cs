using System.Security.Cryptography;
using System.Text;
using SouthBaySoccer.Application.Abstractions.Authentication;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// SHA-256 refresh-token hasher.
/// </summary>
public sealed class RefreshTokenHasher : IRefreshTokenHasher
{
    /// <inheritdoc />
    public string Hash(string rawRefreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var bytes = Encoding.UTF8.GetBytes(rawRefreshToken);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
