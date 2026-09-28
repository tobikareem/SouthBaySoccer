using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

/// <summary>Shared bulk aggregation of approved career or season facts for public player statistics.</summary>
internal sealed class PlayerStatAggregateReader(SouthBaySoccerDbContext dbContext)
{
    /// <summary>
    /// Assembles per-player season or career aggregates from raw match facts using one flat grouped query per
    /// fact type. The previous shape ran six correlated subqueries per grouped player, each
    /// re-inlining the eligible-match join, which made cost scale with players x facts.
    /// </summary>
    internal async Task<IReadOnlyList<PlayerStatAggregate>> ListAsync(
        Guid? seasonId,
        Guid? playerProfileId,
        Guid? groupChatId,
        CancellationToken cancellationToken)
    {
        // Kept as a subquery rather than materialized ids: used once per grouped query below, SQL
        // Server resolves it as a semi-join instead of the per-row lookup the old shape forced.
        var eligibleMatchIds = dbContext.Matches
            .Where(match => match.Status == MatchStatus.Completed || match.Status == MatchStatus.Published || match.Status == MatchStatus.Locked)
            .Join(
                dbContext.Sessions,
                match => match.SessionId,
                session => session.Id,
                (match, session) => new { Match = match, Session = session })
            .Where(x => !seasonId.HasValue || x.Session.SeasonId == seasonId.Value)
            .Select(x => x.Match.Id);

        // Group-scoped leaderboards restrict the player set to members of the selected group chat.
        // Membership drives this filter - the underlying match facts are never group-tagged. Applied
        // as a conditional Where rather than a nullable-IQueryable predicate so it never depends on
        // the provider funcletizing a null check.
        var participants = groupChatId.HasValue
            ? dbContext.PlayerMatchStats.Where(participant => dbContext.PlayerGroupLinks
                .Any(link => link.GroupChatId == groupChatId.Value
                    && link.PlayerProfileId == participant.PlayerProfileId
                    && link.Status == GroupMembershipStatus.Approved))
            : dbContext.PlayerMatchStats;

        var baseRows = await (
            from participant in participants
            join profile in dbContext.PlayerProfiles on participant.PlayerProfileId equals profile.Id
            where participant.Played
                && eligibleMatchIds.Contains(participant.MatchId)
                && (!playerProfileId.HasValue || participant.PlayerProfileId == playerProfileId.Value)
            group participant by new
            {
                profile.Id,
                profile.DisplayName,
                profile.PreferredPosition,
                profile.IsGuest,
                profile.IdentityUserId,
            }
            into grouped
            select new
            {
                grouped.Key.Id,
                grouped.Key.DisplayName,
                grouped.Key.PreferredPosition,
                grouped.Key.IsGuest,
                grouped.Key.IdentityUserId,
                Appearances = grouped.Count(),
                MinutesPlayed = grouped.Sum(x => x.MinutesPlayed ?? 0),
            }).ToArrayAsync(cancellationToken);

        if (baseRows.Length == 0)
        {
            return [];
        }

        // A scoring or assisting player only earns credit for a match they are recorded as having
        // played, which is why both event queries keep the PlayerMatchStats semi-join.
        var goals = await CountByPlayerAsync(
            dbContext.MatchEvents
                .Where(matchEvent => matchEvent.EventType == MatchEventType.Goal
                    && matchEvent.ReviewStatus == MatchEventReviewStatus.Approved
                    && matchEvent.PlayerProfileId != null
                    && (!playerProfileId.HasValue || matchEvent.PlayerProfileId == playerProfileId.Value)
                    && eligibleMatchIds.Contains(matchEvent.MatchId)
                    && dbContext.PlayerMatchStats.Any(x => x.MatchId == matchEvent.MatchId && x.PlayerProfileId == matchEvent.PlayerProfileId && x.Played))
                // Null-forgiving is safe and never actually executed: this is an expression tree,
                // and the predicate above already restricts the set to non-null scorers.
                .Select(matchEvent => matchEvent.PlayerProfileId!.Value),
            cancellationToken);

        var assists = await CountByPlayerAsync(
            dbContext.MatchEvents
                .Where(matchEvent => matchEvent.EventType == MatchEventType.Goal
                    && matchEvent.ReviewStatus == MatchEventReviewStatus.Approved
                    && matchEvent.AssistPlayerProfileId != null
                    && (!playerProfileId.HasValue || matchEvent.AssistPlayerProfileId == playerProfileId.Value)
                    && eligibleMatchIds.Contains(matchEvent.MatchId)
                    && dbContext.PlayerMatchStats.Any(x => x.MatchId == matchEvent.MatchId && x.PlayerProfileId == matchEvent.AssistPlayerProfileId && x.Played))
                // Null-forgiving is safe for the same reason as the scorer projection above.
                .Select(matchEvent => matchEvent.AssistPlayerProfileId!.Value),
            cancellationToken);

        var ratings = (await dbContext.PlayerRatingVotes
                .Where(vote => eligibleMatchIds.Contains(vote.MatchId)
                    && (!playerProfileId.HasValue || vote.RatedPlayerProfileId == playerProfileId.Value))
                .GroupBy(vote => vote.RatedPlayerProfileId)
                .Select(grouped => new
                {
                    PlayerProfileId = grouped.Key,
                    Average = grouped.Average(vote => (decimal?)vote.Score),
                    Count = grouped.Count(),
                })
                .ToArrayAsync(cancellationToken))
            .ToDictionary(row => row.PlayerProfileId, row => new RatingAggregate(row.Average, row.Count));

        var likes = await CountByPlayerAsync(
            dbContext.PlayerLikes
                .Where(like => eligibleMatchIds.Contains(like.MatchId)
                    && (!playerProfileId.HasValue || like.ReceiverPlayerProfileId == playerProfileId.Value))
                .Select(like => like.ReceiverPlayerProfileId),
            cancellationToken);

        var mvpAwards = await CountByPlayerAsync(
            dbContext.MatchAwards
                .Where(award => award.AwardType == MatchAwardType.Mvp
                    && eligibleMatchIds.Contains(award.MatchId)
                    && (!playerProfileId.HasValue || award.PlayerProfileId == playerProfileId.Value))
                .Select(award => award.PlayerProfileId),
            cancellationToken);

        return baseRows.Select(row =>
        {
            var rating = ratings.GetValueOrDefault(row.Id);
            return new PlayerStatAggregate
            {
                PlayerProfileId = row.Id,
                DisplayName = row.DisplayName,
                PreferredPosition = row.PreferredPosition,
                IsGuest = row.IsGuest,
                IdentityUserId = row.IdentityUserId,
                Appearances = row.Appearances,
                MinutesPlayed = row.MinutesPlayed,
                Goals = goals.GetValueOrDefault(row.Id),
                Assists = assists.GetValueOrDefault(row.Id),
                AverageRating = rating?.Average ?? 0m,
                RatingVoteCount = rating?.Count ?? 0,
                Likes = likes.GetValueOrDefault(row.Id),
                MvpAwards = mvpAwards.GetValueOrDefault(row.Id),
            };
        }).ToArray();
    }

    /// <summary>A player's rating totals, absent from the map when they have no votes.</summary>
    private sealed record RatingAggregate(decimal? Average, int Count);

    private static async Task<Dictionary<Guid, int>> CountByPlayerAsync(
        IQueryable<Guid> playerProfileIds,
        CancellationToken cancellationToken) =>
        (await playerProfileIds
            .GroupBy(playerProfileId => playerProfileId)
            .Select(grouped => new { PlayerProfileId = grouped.Key, Count = grouped.Count() })
            .ToArrayAsync(cancellationToken))
        .ToDictionary(row => row.PlayerProfileId, row => row.Count);

}
