namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Application port for issuing application JWT access tokens. The signing and
/// refresh-token implementation lives in Infrastructure.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Creates a signed JWT access token for the specified user and roles.
    /// </summary>
    /// <param name="userId">The identifier of the user the token represents.</param>
    /// <param name="roles">The role names to embed as claims in the token.</param>
    /// <returns>The serialized, signed access token.</returns>
    string CreateAccessToken(Guid userId, IReadOnlyList<string> roles);
}
