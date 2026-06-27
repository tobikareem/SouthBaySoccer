namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Application port for creating and verifying WhatsApp/Pickup Pal sign-in challenges.
/// </summary>
public interface IWhatsAppChallengeService
{
    /// <summary>
    /// Creates a sign-in challenge for the specified phone number and callback URI.
    /// </summary>
    /// <param name="request">The challenge creation request.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The challenge metadata that is safe to return to the client.</returns>
    Task<WhatsAppChallengeIssueResult> CreateChallengeAsync(
        WhatsAppChallengeIssueRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies and consumes a sign-in challenge token.
    /// </summary>
    /// <param name="request">The challenge verification request.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The verified phone details.</returns>
    Task<WhatsAppChallengeVerificationResult> VerifyChallengeAsync(
        WhatsAppChallengeVerificationRequest request,
        CancellationToken cancellationToken = default);
}

