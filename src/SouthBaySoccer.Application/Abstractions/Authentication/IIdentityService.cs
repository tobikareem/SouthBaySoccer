namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Application port over the identity store (ASP.NET Core Identity in Infrastructure).
/// Provides user credential and membership operations without leaking the underlying
/// persistence types into the Application layer.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Verifies a plaintext password against the stored hash for the specified user.
    /// </summary>
    /// <param name="userId">The identifier of the user whose password is being checked.</param>
    /// <param name="password">The candidate plaintext password.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// <see langword="true"/> if the password is valid for the user; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);
}
