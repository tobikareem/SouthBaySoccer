namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Application port for issuing and validating SouthBaySoccer access tokens.
/// The signing implementation lives in Infrastructure.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a signed access token for a verified application principal.
    /// </summary>
    /// <param name="request">The verified principal and authorization claims to place in the token.</param>
    /// <returns>The serialized token and its UTC expiration metadata.</returns>
    IssuedAccessToken IssueAccessToken(AccessTokenIssueRequest request);

    /// <summary>
    /// Validates a serialized access token and returns the principal claims when validation succeeds.
    /// </summary>
    /// <param name="token">The serialized access token supplied by the caller.</param>
    /// <returns>The token validation result.</returns>
    AccessTokenValidationResult ValidateAccessToken(string token);
}

/// <summary>
/// Describes the verified principal that should receive an access token.
/// </summary>
/// <param name="UserId">The application user identifier represented by the token subject.</param>
/// <param name="Roles">The role names to embed in the token.</param>
/// <param name="Policies">The policy names already granted to this principal.</param>
public sealed record AccessTokenIssueRequest(
    Guid UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Policies);

/// <summary>
/// Represents a newly issued access token and non-secret metadata about it.
/// </summary>
/// <param name="Token">The serialized signed token. Treat as a secret and never log it.</param>
/// <param name="ExpiresAtUtc">The UTC instant when the token expires.</param>
/// <param name="KeyId">The signing key identifier used to issue the token.</param>
public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc, string KeyId);

/// <summary>
/// Represents the outcome of access-token validation.
/// </summary>
/// <param name="IsValid">Whether the token is trusted and unexpired.</param>
/// <param name="UserId">The subject user id when validation succeeds.</param>
/// <param name="Roles">The validated role claims.</param>
/// <param name="Policies">The validated policy claims.</param>
/// <param name="ExpiresAtUtc">The UTC expiration when validation succeeds.</param>
/// <param name="KeyId">The signing key identifier that validated the token.</param>
/// <param name="FailureCode">A stable failure code when validation fails.</param>
public sealed record AccessTokenValidationResult(
    bool IsValid,
    Guid? UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Policies,
    DateTime? ExpiresAtUtc,
    string? KeyId,
    string? FailureCode)
{
    /// <summary>
    /// Creates a failed validation result with no principal claims.
    /// </summary>
    /// <param name="failureCode">A stable failure code safe for diagnostics.</param>
    /// <returns>A failed token validation result.</returns>
    public static AccessTokenValidationResult Failed(string failureCode) =>
        new(false, null, Array.Empty<string>(), Array.Empty<string>(), null, null, failureCode);

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="userId">The validated subject user id.</param>
    /// <param name="roles">The validated role claims.</param>
    /// <param name="policies">The validated policy claims.</param>
    /// <param name="expiresAtUtc">The UTC expiration instant.</param>
    /// <param name="keyId">The signing key identifier that validated the token.</param>
    /// <returns>A successful token validation result.</returns>
    public static AccessTokenValidationResult Succeeded(
        Guid userId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> policies,
        DateTime expiresAtUtc,
        string keyId) =>
        new(true, userId, roles, policies, expiresAtUtc, keyId, null);
}
