using System;
using System.Collections.Generic;

namespace SouthBaySoccer.Contracts.UserActivity;

/// <summary>An owner-only page of successful authentication activity.</summary>
public sealed record UserActivityPageDto(
    IReadOnlyList<UserActivityEntryDto> Items,
    int Page,
    int PageSize,
    bool HasMore,
    DateTime? TrackingStartedAtUtc);

/// <summary>A successful sign-up or sign-in and the player's recorded activity summary.</summary>
public sealed record UserActivityEntryDto(
    Guid Id,
    Guid PlayerProfileId,
    string DisplayName,
    string ActivityType,
    DateTime OccurredAtUtc,
    DateTime FirstRecordedActivityAtUtc,
    DateTime LastRecordedActivityAtUtc,
    long SignInCount,
    IReadOnlyList<UserActivityGroupDto> Groups);

/// <summary>The player's current group membership, not a historical membership snapshot.</summary>
public sealed record UserActivityGroupDto(string GroupName, string Status);
