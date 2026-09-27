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

public sealed record PlayerProfileDetailModel(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    string Initials,
    CareerStatsModel CareerStats,
    IReadOnlyList<PlayerProfileRecentFormOutcome> RecentForm,
    string? PendingConfirmationNote,
    string Role);

public sealed record CareerStatsModel(
    int Matches,
    int Goals,
    int Assists,
    decimal AverageRating,
    int MvpAwards,
    int Likes,
    int Wins = 0,
    int Losses = 0);

public enum PlayerProfileRecentFormOutcome
{
    Win,
    Draw,
    Loss
}

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
