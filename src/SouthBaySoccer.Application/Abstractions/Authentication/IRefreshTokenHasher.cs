namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Hashes refresh-token secrets before persistence or lookup.
/// </summary>
public interface IRefreshTokenHasher
{
    /// <summary>
    /// Creates a deterministic hash for a raw refresh token.
    /// </summary>
    /// <param name="rawRefreshToken">The raw refresh token secret.</param>
    /// <returns>The token hash safe for persistence and lookup.</returns>
    string Hash(string rawRefreshToken);
}
