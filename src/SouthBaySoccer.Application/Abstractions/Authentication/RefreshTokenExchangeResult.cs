namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Represents the outcome of a refresh-token exchange.
/// </summary>
/// <param name="Status">The exchange status.</param>
/// <param name="IdentityUserId">The authenticated identity user id when rotation succeeds.</param>
/// <param name="PlayerProfileId">The linked player profile id when available.</param>
/// <param name="RefreshToken">The replacement raw refresh token when rotation succeeds.</param>
/// <param name="RefreshTokenId">The replacement refresh-token record id when rotation succeeds.</param>
/// <param name="RefreshTokenExpiresAtUtc">The replacement refresh-token expiry when rotation succeeds.</param>
public sealed record RefreshTokenExchangeResult(
    RefreshTokenExchangeStatus Status,
    Guid? IdentityUserId = null,
    Guid? PlayerProfileId = null,
    string? RefreshToken = null,
    Guid? RefreshTokenId = null,
    DateTime? RefreshTokenExpiresAtUtc = null)
{
    /// <summary>Gets a value indicating whether the exchange produced a replacement token.</summary>
    public bool Succeeded => Status == RefreshTokenExchangeStatus.Rotated;
}
