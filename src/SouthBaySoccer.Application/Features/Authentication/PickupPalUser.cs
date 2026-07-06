namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents a user profile returned by Pickup Pal, the source of truth for user identity data.
/// </summary>
public sealed record PickupPalUser(
    string Id,
    string? Email,
    string PhoneNumber,
    string? FirstName,
    string? LastName,
    string? NickName,
    string? ProfilePicture,
    IReadOnlyList<string> PreferredPositions,
    DateTime? UpdatedAtUtc);
