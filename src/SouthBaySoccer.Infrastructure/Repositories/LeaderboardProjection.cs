using System;
using System.Collections.Generic;
using System.Linq;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Infrastructure.Repositories;

/// <summary>One player's derived season totals, assembled from raw match facts.</summary>
internal sealed class PlayerStatAggregate
{
    public Guid PlayerProfileId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string PreferredPosition { get; set; } = string.Empty;

    public bool IsGuest { get; set; }

    public Guid? IdentityUserId { get; set; }

    public int Appearances { get; set; }

    public int MinutesPlayed { get; set; }

    public int Goals { get; set; }

    public int Assists { get; set; }

    public decimal AverageRating { get; set; }

    public int RatingVoteCount { get; set; }

    public int Likes { get; set; }

    public int MvpAwards { get; set; }
}

/// <summary>
/// Ranking rules for the season leaderboard, kept separate from the queries that feed it so the
/// ordering contract can be tested without a database.
/// </summary>
internal static class LeaderboardProjection
{
    /// <summary>
    /// Orders aggregates by the requested metric and its documented tie-breaks. Display-name
    /// comparison is invariant and case-insensitive so the result does not vary with machine
    /// locale; the trailing player-id comparison makes the order total regardless.
    /// </summary>
    internal static IReadOnlyList<PlayerStatAggregate> Order(
        IEnumerable<PlayerStatAggregate> aggregates,
        StatLeaderboardMetric metric) =>
        metric switch
        {
            StatLeaderboardMetric.Goals => aggregates
                .OrderByDescending(x => x.Goals)
                .ThenBy(x => x.Appearances)
                .ThenByDescending(x => x.Assists)
                .ThenByName()
                .ToArray(),
            StatLeaderboardMetric.Assists => aggregates
                .OrderByDescending(x => x.Assists)
                .ThenBy(x => x.MinutesPlayed)
                .ThenByDescending(x => x.Goals)
                .ThenByName()
                .ToArray(),
            StatLeaderboardMetric.Rating => aggregates
                .OrderByDescending(x => x.AverageRating)
                .ThenByDescending(x => x.RatingVoteCount)
                .ThenByDescending(x => x.Appearances)
                .ThenByName()
                .ToArray(),
            StatLeaderboardMetric.Mvp => aggregates
                .OrderByDescending(x => x.MvpAwards)
                .ThenBy(x => x.Appearances)
                .ThenByDescending(x => x.AverageRating)
                .ThenByName()
                .ToArray(),
            _ => aggregates
                .OrderBy(x => x.DisplayName, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(x => x.PlayerProfileId)
                .ToArray(),
        };

    /// <summary>Gets the value the leaderboard row reports for the requested metric.</summary>
    internal static decimal GetMetricValue(PlayerStatAggregate row, StatLeaderboardMetric metric) =>
        metric switch
        {
            StatLeaderboardMetric.Goals => row.Goals,
            StatLeaderboardMetric.Assists => row.Assists,
            StatLeaderboardMetric.Rating => row.AverageRating,
            StatLeaderboardMetric.Mvp => row.MvpAwards,
            _ => 0m,
        };

    private static IOrderedEnumerable<PlayerStatAggregate> ThenByName(
        this IOrderedEnumerable<PlayerStatAggregate> ordered) =>
        ordered
            .ThenBy(x => x.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(x => x.PlayerProfileId);
}
