namespace SouthBaySoccer.Contracts.Authentication;

public sealed record AuthenticationTokensResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc);
