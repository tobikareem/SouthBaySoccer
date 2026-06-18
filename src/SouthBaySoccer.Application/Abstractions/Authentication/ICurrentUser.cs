namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Exposes the authenticated principal for the current request so use cases can
/// make authorization decisions and stamp audit fields. This is ambient request
/// context, never a security boundary on its own; the entry layer still enforces access.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the identifier of the authenticated user, or <see langword="null"/> when the
    /// request is anonymous.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Gets a value indicating whether the current request is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Determines whether the current user is a member of the specified role.
    /// </summary>
    /// <param name="role">The role name to test, for example <c>Owner</c> or <c>Player</c>.</param>
    /// <returns><see langword="true"/> if the user holds the role; otherwise <see langword="false"/>.</returns>
    bool IsInRole(string role);

    /// <summary>
    /// Determines whether the current user satisfies the specified authorization policy.
    /// </summary>
    /// <param name="policy">The policy name to evaluate, for example <c>CanManageSessions</c>.</param>
    /// <returns><see langword="true"/> if the policy is satisfied; otherwise <see langword="false"/>.</returns>
    bool HasPolicy(string policy);
}
