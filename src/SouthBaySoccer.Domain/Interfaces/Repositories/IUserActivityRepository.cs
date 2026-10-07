using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>Bounded owner activity reads restricted to active player profiles.</summary>
public interface IUserActivityRepository
{
    /// <summary>Reads a page and retained history/current membership summaries for its players.</summary>
    Task<UserActivityPageReadModel> ReadPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>A page of activity with the earliest retained visible activity timestamp.</summary>
/// <param name="Items">The bounded activity rows.</param>
/// <param name="HasMore">Whether another page exists.</param>
/// <param name="TrackingStartedAtUtc">Earliest retained visible activity, if any.</param>
public sealed record UserActivityPageReadModel(IReadOnlyList<UserActivityReadModel> Items, bool HasMore, DateTime? TrackingStartedAtUtc);

/// <summary>Activity and aggregate history for a public player reference.</summary>
/// <param name="Id">The event id.</param>
/// <param name="PlayerProfileId">The active player reference.</param>
/// <param name="DisplayName">The current player name.</param>
/// <param name="ActivityType">The authentication kind.</param>
/// <param name="OccurredAtUtc">The UTC issuance timestamp.</param>
/// <param name="FirstRecordedActivityAtUtc">The first retained activity timestamp.</param>
/// <param name="LastRecordedActivityAtUtc">The last retained activity timestamp.</param>
/// <param name="SignInCount">The retained sign-in count, excluding sign-ups.</param>
/// <param name="Groups">Current memberships.</param>
public sealed record UserActivityReadModel(
    Guid Id, Guid PlayerProfileId, string DisplayName, UserActivityType ActivityType,
    DateTime OccurredAtUtc, DateTime FirstRecordedActivityAtUtc, DateTime LastRecordedActivityAtUtc,
    long SignInCount, IReadOnlyList<UserActivityGroupReadModel> Groups);

/// <summary>A current group membership state.</summary>
/// <param name="GroupName">The current group name.</param>
/// <param name="Status">The current membership state.</param>
public sealed record UserActivityGroupReadModel(string GroupName, string Status);
