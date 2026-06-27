namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Application port for exchanging a verified sign-in subject for application session tokens.
/// </summary>
public interface IAuthenticationTokenIssuer
{
    /// <summary>
    /// Issues access and refresh tokens for the specified authenticated subject.
    /// </summary>
    /// <param name="subject">The authenticated subject.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The issued token set.</returns>
    Task<AuthenticationTokenSet> IssueTokensAsync(
        AuthenticationTokenSubject subject,
        CancellationToken cancellationToken = default);
}

