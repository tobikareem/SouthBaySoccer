namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Generates cryptographically strong raw refresh-token secrets.
/// </summary>
public interface IRefreshTokenSecretGenerator
{
    /// <summary>
    /// Creates a new raw refresh token secret for return to the client.
    /// </summary>
    /// <returns>The raw refresh token secret.</returns>
    string CreateToken();
}
