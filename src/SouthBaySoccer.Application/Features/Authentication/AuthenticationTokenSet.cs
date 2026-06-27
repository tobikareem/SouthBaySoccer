namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents application session tokens issued after a trusted authentication exchange.
/// </summary>
/// <param name="AccessToken">The short-lived bearer access token.</param>
/// <param name="RefreshToken">The rotating refresh token.</param>
/// <param name="AccessTokenExpiresAtUtc">The UTC access-token expiration timestamp.</param>
public sealed record AuthenticationTokenSet(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc);

