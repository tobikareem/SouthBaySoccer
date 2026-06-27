namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>
/// Represents a refresh-token exchange request.
/// </summary>
/// <param name="RefreshToken">The raw refresh token presented by the client.</param>
/// <param name="DeviceId">The optional stable client device/session identifier.</param>
/// <param name="UserAgent">The optional request user agent; stored only as a hash.</param>
/// <param name="IpAddress">The optional remote IP address; stored only as a hash.</param>
public sealed record RefreshTokenExchangeRequest(
    string RefreshToken,
    string? DeviceId = null,
    string? UserAgent = null,
    string? IpAddress = null);
