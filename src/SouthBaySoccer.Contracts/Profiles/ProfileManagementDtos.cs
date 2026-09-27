namespace SouthBaySoccer.Contracts.Profiles;

public sealed record EmergencyContactRequest(
    string Name,
    string PhoneNumber,
    string? Relationship);

public sealed record EmergencyContactResponse(
    string Name,
    string MaskedPhoneNumber,
    string? Relationship);

public sealed record MyProfileResponse(
    Guid PlayerProfileId,
    Guid? IdentityUserId,
    string DisplayName,
    string PreferredPosition,
    string? PhotoUri,
    bool IsGuest,
    string Role,
    EmergencyContactResponse? EmergencyContact,
    CareerStatsDto? CareerStats = null,
    IReadOnlyList<MatchResult>? RecentForm = null);

public sealed record UpdateMyProfileRequest(
    string DisplayName,
    string PreferredPosition,
    string? PhotoUri,
    EmergencyContactRequest? EmergencyContact);

public sealed record CreateGuestProfileRequest(
    string DisplayName,
    string PreferredPosition,
    string? PhotoUri,
    EmergencyContactRequest? EmergencyContact);

public sealed record CreateProfileMergeRequest(
    Guid SourceGuestPlayerProfileId,
    Guid TargetPlayerProfileId);

public sealed record ProfileMergeResponse(
    Guid ProfileMergeId,
    Guid SourceGuestPlayerProfileId,
    Guid TargetPlayerProfileId,
    string Status);
