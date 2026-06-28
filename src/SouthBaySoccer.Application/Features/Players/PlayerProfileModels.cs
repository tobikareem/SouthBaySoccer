namespace SouthBaySoccer.Application.Features.Players;

public sealed record EmergencyContactModel(
    string Name,
    string PhoneNumber,
    string? Relationship);

public sealed record PlayerProfileModel(
    Guid PlayerProfileId,
    Guid? IdentityUserId,
    string DisplayName,
    string PreferredPosition,
    string? PhotoUri,
    bool IsGuest,
    string Role,
    EmergencyContactView? EmergencyContact);

public sealed record EmergencyContactView(
    string Name,
    string MaskedPhoneNumber,
    string? Relationship);

public sealed record UpdateMyProfileCommand(
    string DisplayName,
    string PreferredPosition,
    string? PhotoUri,
    EmergencyContactModel? EmergencyContact);

public sealed record CreateGuestProfileCommand(
    string DisplayName,
    string PreferredPosition,
    string? PhotoUri,
    EmergencyContactModel? EmergencyContact);

public sealed record CreateProfileMergeCommand(
    Guid SourceGuestPlayerProfileId,
    Guid TargetPlayerProfileId);

public sealed record ProfileMergeResult(
    Guid ProfileMergeId,
    Guid SourceGuestPlayerProfileId,
    Guid TargetPlayerProfileId,
    string Status);
