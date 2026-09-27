namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Revokes refresh tokens so a device must sign in (and re-verify) again. Revocation is recorded on
/// the immutable refresh-token history; tokens are never deleted.
/// </summary>
public interface IRefreshTokenRevocationService
{
    /// <summary>Revokes the presented refresh token and every token in its rotation family.</summary>
    /// <param name="identityUserId">The identity user the token must belong to.</param>
    /// <param name="refreshToken">The raw refresh token presented by the client.</param>
    /// <param name="reason">A short, non-personal reason stored on the record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RevokeFamilyAsync(
        Guid identityUserId,
        string refreshToken,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes every active refresh token the identity user owns.</summary>
    /// <param name="identityUserId">The identity user.</param>
    /// <param name="reason">A short, non-personal reason stored on the records.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RevokeAllAsync(Guid identityUserId, string reason, CancellationToken cancellationToken = default);
}
