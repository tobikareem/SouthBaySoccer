using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationCoordinator
{
    /// <summary>
    /// Whether sign-in has already completed — via the manual phone flow, a verified app-link
    /// callback, or a claimed session restore — and the authenticated Shell has been requested.
    /// Startup session restore checks this before doing any work so it never races an
    /// already-completed sign-in for the Shell swap.
    /// </summary>
    bool IsAuthenticated { get; }

    Task CompleteSignInAsync(
        AuthenticationTokensResponse tokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the current session: clears the persisted tokens, resets <see cref="IsAuthenticated"/>
    /// to false, and returns to the sign-in screen so a different account can sign in.
    /// </summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task<bool> TryCompleteChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken = default);

    Task<bool> HandleCallbackAsync(Uri callbackUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims sign-in completion for a caller that persists tokens and shows the
    /// authenticated Shell itself (currently: startup session restore). Exactly one of
    /// <see cref="CompleteSignInAsync"/>, <see cref="HandleCallbackAsync"/>,
    /// <see cref="TryCompleteChallengeAsync"/>, and this method ever wins, regardless of the
    /// order they land in.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the caller won the race and must proceed with its own token
    /// persistence and Shell navigation; <see langword="false"/> if sign-in already completed
    /// elsewhere, in which case the caller must not navigate.
    /// </returns>
    Task<bool> TryClaimAuthenticationAsync(CancellationToken cancellationToken = default);
}
