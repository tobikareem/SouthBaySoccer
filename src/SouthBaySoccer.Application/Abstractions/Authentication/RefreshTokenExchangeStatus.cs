namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Refresh-token exchange outcomes.
/// </summary>
public enum RefreshTokenExchangeStatus
{
    /// <summary>The token was active and was rotated.</summary>
    Rotated,
    /// <summary>The token was not found or the request was malformed.</summary>
    Invalid,
    /// <summary>The token was already consumed and reuse was detected.</summary>
    Reused,
    /// <summary>The token was expired.</summary>
    Expired,
    /// <summary>The token or its family was already revoked.</summary>
    Revoked,
}
