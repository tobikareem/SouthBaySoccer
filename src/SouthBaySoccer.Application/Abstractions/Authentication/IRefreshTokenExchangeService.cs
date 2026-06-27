namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Exchanges refresh tokens using atomic rotation and reuse detection.
/// </summary>
public interface IRefreshTokenExchangeService
{
    /// <summary>
    /// Rotates an active refresh token, or revokes the token family when reuse is detected.
    /// </summary>
    /// <param name="request">The refresh-token exchange request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The refresh-token exchange result.</returns>
    Task<RefreshTokenExchangeResult> RotateAsync(
        RefreshTokenExchangeRequest request,
        CancellationToken cancellationToken = default);
}
