using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class StatsRepository(SouthBaySoccerDbContext dbContext) : IStatsRepository
{
    public async Task<Match> CreateMatchAsync(
        Match match,
        IReadOnlyList<MatchTeam> teams,
        IReadOnlyList<TeamAssignment> assignments,
        IReadOnlyList<PlayerMatchStats> participants,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Matches.AddAsync(match, cancellationToken);
        await dbContext.MatchTeams.AddRangeAsync(teams, cancellationToken);
        await dbContext.TeamAssignments.AddRangeAsync(assignments, cancellationToken);
        await dbContext.PlayerMatchStats.AddRangeAsync(participants, cancellationToken);
        return match;
    }

    public Task<Match?> FindMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        dbContext.Matches.SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken);

    public Task<Match?> FindPrimaryMatchBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        dbContext.Matches
            .OrderBy(x => x.MatchNumber)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

    public async Task<IReadOnlyList<MatchTeam>> ListMatchTeamsAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await dbContext.MatchTeams.Where(x => x.MatchId == matchId).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<TeamAssignment>> ListAssignmentsAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await dbContext.TeamAssignments.Where(x => x.MatchId == matchId).ToArrayAsync(cancellationToken);

    public Task<MatchEvent?> FindMatchEventAsync(Guid matchEventId, CancellationToken cancellationToken = default) =>
        dbContext.MatchEvents.SingleOrDefaultAsync(x => x.Id == matchEventId, cancellationToken);

    public async Task<IReadOnlyList<MatchEvent>> ListMatchEventsAsync(
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        await dbContext.MatchEvents
            .Where(x => x.MatchId == matchId)
            .OrderBy(x => x.Minute)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchResult>> ListMatchResultsAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await dbContext.MatchResults.Where(x => x.MatchId == matchId).ToArrayAsync(cancellationToken);

    public async Task ReplaceMatchEventsAsync(
        Guid matchId,
        IReadOnlyList<MatchEvent> events,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.MatchEvents.Where(x => x.MatchId == matchId).ToArrayAsync(cancellationToken);
        foreach (var matchEvent in existing)
        {
            matchEvent.IsDeleted = true;
        }

        await dbContext.MatchEvents.AddRangeAsync(events, cancellationToken);
    }

    public async Task ReplaceOwnPendingMatchEventsAsync(
        Guid matchId,
        Guid submittedByPlayerProfileId,
        IReadOnlyList<MatchEvent> events,
        CancellationToken cancellationToken = default)
    {
        // Scope the soft-delete to this player's own still-pending claim. Approved/rejected rows are
        // settled facts, and other players' rows are none of this submitter's business.
        var existing = await dbContext.MatchEvents
            .Where(x => x.MatchId == matchId
                && x.SubmittedByPlayerProfileId == submittedByPlayerProfileId
                && x.ReviewStatus == MatchEventReviewStatus.Pending)
            .ToArrayAsync(cancellationToken);
        foreach (var matchEvent in existing)
        {
            matchEvent.IsDeleted = true;
        }

        await dbContext.MatchEvents.AddRangeAsync(events, cancellationToken);
    }

    public async Task EnsurePlayerMatchParticipationAsync(
        Guid matchId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.PlayerMatchStats
            .AnyAsync(x => x.MatchId == matchId && x.PlayerProfileId == playerProfileId, cancellationToken);
        if (exists)
        {
            return;
        }

        await dbContext.PlayerMatchStats.AddAsync(new PlayerMatchStats
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerProfileId = playerProfileId,
            Played = true,
        }, cancellationToken);
    }

    public async Task UpsertMatchResultsAsync(
        Guid matchId,
        IReadOnlyList<MatchResult> results,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.MatchResults.Where(x => x.MatchId == matchId).ToArrayAsync(cancellationToken);
        foreach (var result in results)
        {
            var current = existing.SingleOrDefault(x => x.MatchTeamId == result.MatchTeamId);
            if (current is null)
            {
                await dbContext.MatchResults.AddAsync(result, cancellationToken);
                continue;
            }

            current.Wins = result.Wins;
            current.Draws = result.Draws;
            current.Losses = result.Losses;
            current.GoalsFor = result.GoalsFor;
            current.GoalsAgainst = result.GoalsAgainst;
        }
    }

    public async Task ReplaceCaptainTopologyAsync(
        Guid matchId,
        IReadOnlyList<MatchTeam> teams,
        IReadOnlyList<TeamAssignment> assignments,
        IReadOnlyList<PlayerMatchStats> participants,
        CancellationToken cancellationToken = default)
    {
        var existingTeams = await dbContext.MatchTeams
            .Where(x => x.MatchId == matchId)
            .ToArrayAsync(cancellationToken);
        var existingAssignments = await dbContext.TeamAssignments
            .Where(x => x.MatchId == matchId)
            .ToArrayAsync(cancellationToken);
        var existingParticipants = await dbContext.PlayerMatchStats
            .Where(x => x.MatchId == matchId)
            .ToArrayAsync(cancellationToken);

        foreach (var team in existingTeams)
        {
            team.IsDeleted = true;
        }

        foreach (var assignment in existingAssignments)
        {
            assignment.IsDeleted = true;
        }

        foreach (var participant in existingParticipants)
        {
            participant.IsDeleted = true;
        }

        await dbContext.MatchTeams.AddRangeAsync(teams, cancellationToken);
        await dbContext.TeamAssignments.AddRangeAsync(assignments, cancellationToken);
        await dbContext.PlayerMatchStats.AddRangeAsync(participants, cancellationToken);
    }

    public async Task ReplaceTeamAssignmentsAsync(
        Guid matchId,
        Guid matchTeamId,
        IReadOnlyList<Guid> playerProfileIds,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = playerProfileIds.ToHashSet();
        var teamAssignments = await dbContext.TeamAssignments
            .Where(x => x.MatchId == matchId && x.MatchTeamId == matchTeamId)
            .ToArrayAsync(cancellationToken);
        var existingTeamIds = teamAssignments.Select(x => x.PlayerProfileId).ToHashSet();

        foreach (var assignment in teamAssignments.Where(x => !requestedIds.Contains(x.PlayerProfileId)))
        {
            assignment.IsDeleted = true;
        }

        await dbContext.TeamAssignments.AddRangeAsync(
            requestedIds
                .Where(playerId => !existingTeamIds.Contains(playerId))
                .Select(playerId => new TeamAssignment
                {
                    Id = Guid.NewGuid(),
                    MatchId = matchId,
                    MatchTeamId = matchTeamId,
                    PlayerProfileId = playerId,
                }),
            cancellationToken);

        var otherAssignedIds = await dbContext.TeamAssignments
            .Where(x => x.MatchId == matchId && x.MatchTeamId != matchTeamId)
            .Select(x => x.PlayerProfileId)
            .ToArrayAsync(cancellationToken);
        var activePlayerIds = otherAssignedIds.Concat(requestedIds).ToHashSet();
        var participants = await dbContext.PlayerMatchStats
            .Where(x => x.MatchId == matchId)
            .ToArrayAsync(cancellationToken);
        var participantIds = participants.Select(x => x.PlayerProfileId).ToHashSet();

        foreach (var participant in participants.Where(x => !activePlayerIds.Contains(x.PlayerProfileId)))
        {
            participant.IsDeleted = true;
        }

        await dbContext.PlayerMatchStats.AddRangeAsync(
            activePlayerIds
                .Where(playerId => !participantIds.Contains(playerId))
                .Select(playerId => new PlayerMatchStats
                {
                    Id = Guid.NewGuid(),
                    MatchId = matchId,
                    PlayerProfileId = playerId,
                    Played = true,
                    Started = true,
                }),
            cancellationToken);
    }

    public async Task SubmitPeerFeedbackAsync(
        Guid matchId,
        Guid voterPlayerProfileId,
        IReadOnlyList<PlayerRatingVote> votes,
        IReadOnlyList<PlayerLike> likes,
        MatchAward? mvpAward,
        CancellationToken cancellationToken = default)
    {
        var existingVotes = await dbContext.PlayerRatingVotes
            .Where(x => x.MatchId == matchId && x.VoterPlayerProfileId == voterPlayerProfileId)
            .ToArrayAsync(cancellationToken);
        foreach (var vote in existingVotes)
        {
            vote.IsDeleted = true;
        }

        var existingLikes = await dbContext.PlayerLikes
            .Where(x => x.MatchId == matchId && x.GiverPlayerProfileId == voterPlayerProfileId)
            .ToArrayAsync(cancellationToken);
        foreach (var like in existingLikes)
        {
            like.IsDeleted = true;
        }

        await dbContext.PlayerRatingVotes.AddRangeAsync(votes, cancellationToken);
        await dbContext.PlayerLikes.AddRangeAsync(likes, cancellationToken);

        if (mvpAward is null)
        {
            return;
        }

        var existingMvp = await dbContext.MatchAwards
            .SingleOrDefaultAsync(x => x.MatchId == matchId && x.AwardType == MatchAwardType.Mvp, cancellationToken);
        if (existingMvp is not null)
        {
            existingMvp.IsDeleted = true;
        }

        await dbContext.MatchAwards.AddAsync(mvpAward, cancellationToken);
    }

    public Task AddStatCorrectionAsync(StatCorrection correction, CancellationToken cancellationToken = default) =>
        dbContext.StatCorrections.AddAsync(correction, cancellationToken).AsTask();

    public Task AddProfileStatReassignmentAuditAsync(ProfileStatReassignmentAudit audit, CancellationToken cancellationToken = default) =>
        dbContext.ProfileStatReassignmentAudits.AddAsync(audit, cancellationToken).AsTask();

    public async Task<int> ReassignProfileStatsAsync(
        Guid sourcePlayerProfileId,
        Guid targetPlayerProfileId,
        CancellationToken cancellationToken = default)
    {
        var affected = 0;

        affected += await ReassignUniqueRowsAsync(
            await dbContext.TeamAssignments.Where(x => x.PlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.TeamAssignments.Any(x => x.MatchId == row.MatchId && x.PlayerProfileId == targetPlayerProfileId),
            row => row.PlayerProfileId = targetPlayerProfileId);

        affected += await ReassignUniqueRowsAsync(
            await dbContext.PlayerMatchStats.Where(x => x.PlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.PlayerMatchStats.Any(x => x.MatchId == row.MatchId && x.PlayerProfileId == targetPlayerProfileId),
            row => row.PlayerProfileId = targetPlayerProfileId);

        foreach (var matchEvent in await dbContext.MatchEvents.Where(x => x.PlayerProfileId == sourcePlayerProfileId || x.AssistPlayerProfileId == sourcePlayerProfileId || x.SubmittedByPlayerProfileId == sourcePlayerProfileId || x.ReviewedByPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken))
        {
            if (matchEvent.PlayerProfileId == sourcePlayerProfileId)
            {
                matchEvent.PlayerProfileId = targetPlayerProfileId;
                affected++;
            }

            if (matchEvent.AssistPlayerProfileId == sourcePlayerProfileId)
            {
                matchEvent.AssistPlayerProfileId = targetPlayerProfileId;
                affected++;
            }

            if (matchEvent.SubmittedByPlayerProfileId == sourcePlayerProfileId)
            {
                matchEvent.SubmittedByPlayerProfileId = targetPlayerProfileId;
                affected++;
            }

            if (matchEvent.ReviewedByPlayerProfileId == sourcePlayerProfileId)
            {
                matchEvent.ReviewedByPlayerProfileId = targetPlayerProfileId;
                affected++;
            }
        }

        affected += await ReassignUniqueRowsAsync(
            await dbContext.PlayerRatingVotes.Where(x => x.VoterPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.PlayerRatingVotes.Any(x => x.MatchId == row.MatchId && x.VoterPlayerProfileId == targetPlayerProfileId && x.RatedPlayerProfileId == row.RatedPlayerProfileId),
            row => row.VoterPlayerProfileId = targetPlayerProfileId);

        affected += await ReassignUniqueRowsAsync(
            await dbContext.PlayerRatingVotes.Where(x => x.RatedPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.PlayerRatingVotes.Any(x => x.MatchId == row.MatchId && x.VoterPlayerProfileId == row.VoterPlayerProfileId && x.RatedPlayerProfileId == targetPlayerProfileId),
            row => row.RatedPlayerProfileId = targetPlayerProfileId);

        affected += await ReassignUniqueRowsAsync(
            await dbContext.PlayerLikes.Where(x => x.GiverPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.PlayerLikes.Any(x => x.MatchId == row.MatchId && x.GiverPlayerProfileId == targetPlayerProfileId && x.ReceiverPlayerProfileId == row.ReceiverPlayerProfileId),
            row => row.GiverPlayerProfileId = targetPlayerProfileId);

        affected += await ReassignUniqueRowsAsync(
            await dbContext.PlayerLikes.Where(x => x.ReceiverPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.PlayerLikes.Any(x => x.MatchId == row.MatchId && x.GiverPlayerProfileId == row.GiverPlayerProfileId && x.ReceiverPlayerProfileId == targetPlayerProfileId),
            row => row.ReceiverPlayerProfileId = targetPlayerProfileId);

        foreach (var award in await dbContext.MatchAwards.Where(x => x.PlayerProfileId == sourcePlayerProfileId || x.AwardedByPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken))
        {
            if (award.PlayerProfileId == sourcePlayerProfileId)
            {
                award.PlayerProfileId = targetPlayerProfileId;
                affected++;
            }

            if (award.AwardedByPlayerProfileId == sourcePlayerProfileId)
            {
                award.AwardedByPlayerProfileId = targetPlayerProfileId;
                affected++;
            }
        }

        foreach (var correction in await dbContext.StatCorrections.Where(x => x.PlayerProfileId == sourcePlayerProfileId || x.CorrectedByPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken))
        {
            if (correction.PlayerProfileId == sourcePlayerProfileId)
            {
                correction.PlayerProfileId = targetPlayerProfileId;
                affected++;
            }

            if (correction.CorrectedByPlayerProfileId == sourcePlayerProfileId)
            {
                correction.CorrectedByPlayerProfileId = targetPlayerProfileId;
                affected++;
            }
        }

        // Roster membership: the imported snapshot row and the check-in both key a player onto a
        // session by profile id, so a merge that skipped these left the target off the roster (the
        // very bug that made a merged player unable to submit stats). Dedup on (session, profile).
        affected += await ReassignUniqueRowsAsync(
            await dbContext.Set<PickupPalGameParticipant>().Where(x => x.PlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.Set<PickupPalGameParticipant>().Any(x => x.SessionId == row.SessionId && x.PlayerProfileId == targetPlayerProfileId),
            row => row.PlayerProfileId = targetPlayerProfileId);

        affected += await ReassignUniqueRowsAsync(
            await dbContext.CheckIns.Where(x => x.PlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken),
            row => dbContext.CheckIns.Any(x => x.SessionId == row.SessionId && x.PlayerProfileId == targetPlayerProfileId),
            row => row.PlayerProfileId = targetPlayerProfileId);

        foreach (var checkIn in await dbContext.CheckIns.Where(x => x.CheckedInByPlayerProfileId == sourcePlayerProfileId).ToArrayAsync(cancellationToken))
        {
            checkIn.CheckedInByPlayerProfileId = targetPlayerProfileId;
            affected++;
        }

        return affected;
    }


    public async Task<IReadOnlyList<LeaderboardReadModel>> ListSeasonLeaderboardAsync(
        Guid seasonId,
        StatLeaderboardMetric metric,
        int skip,
        int take,
        Guid? groupChatId,
        CancellationToken cancellationToken = default)
    {
        var query = BuildPlayerStatAggregates(seasonId, playerProfileId: null, groupChatId);
        var ordered = metric switch
        {
            StatLeaderboardMetric.Goals => query
                .OrderByDescending(x => x.Goals)
                .ThenBy(x => x.Appearances)
                .ThenByDescending(x => x.Assists)
                .ThenBy(x => x.DisplayName)
                .ThenBy(x => x.PlayerProfileId),
            StatLeaderboardMetric.Assists => query
                .OrderByDescending(x => x.Assists)
                .ThenBy(x => x.MinutesPlayed)
                .ThenByDescending(x => x.Goals)
                .ThenBy(x => x.DisplayName)
                .ThenBy(x => x.PlayerProfileId),
            StatLeaderboardMetric.Rating => query
                .OrderByDescending(x => x.AverageRating)
                .ThenByDescending(x => x.RatingVoteCount)
                .ThenByDescending(x => x.Appearances)
                .ThenBy(x => x.DisplayName)
                .ThenBy(x => x.PlayerProfileId),
            StatLeaderboardMetric.Mvp => query
                .OrderByDescending(x => x.MvpAwards)
                .ThenBy(x => x.Appearances)
                .ThenByDescending(x => x.AverageRating)
                .ThenBy(x => x.DisplayName)
                .ThenBy(x => x.PlayerProfileId),
            _ => query.OrderBy(x => x.DisplayName).ThenBy(x => x.PlayerProfileId),
        };

        var rows = await ordered.Skip(skip).Take(take).ToArrayAsync(cancellationToken);
        return rows.Select(row => new LeaderboardReadModel(
            row.PlayerProfileId,
            row.DisplayName,
            row.PreferredPosition,
            row.IsGuest,
            row.IdentityUserId,
            row.Appearances,
            row.Goals,
            row.Assists,
            row.AverageRating,
            row.RatingVoteCount,
            row.Likes,
            row.MvpAwards,
            GetMetricValue(row, metric))).ToArray();
    }

    public async Task<PlayerStatSummaryReadModel?> GetPlayerStatsAsync(
        Guid playerProfileId,
        Guid? seasonId,
        CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.PlayerProfiles
            .Where(x => x.Id == playerProfileId)
            .Select(x => new { x.Id, x.DisplayName, x.PreferredPosition, x.IsGuest, x.IdentityUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var aggregate = await BuildPlayerStatAggregates(seasonId, playerProfileId).SingleOrDefaultAsync(cancellationToken);
        return new PlayerStatSummaryReadModel(
            profile.Id,
            profile.DisplayName,
            profile.PreferredPosition,
            profile.IsGuest,
            profile.IdentityUserId,
            aggregate?.Appearances ?? 0,
            aggregate?.Goals ?? 0,
            aggregate?.Assists ?? 0,
            aggregate?.AverageRating ?? 0m,
            aggregate?.RatingVoteCount ?? 0,
            aggregate?.Likes ?? 0,
            aggregate?.MvpAwards ?? 0);
    }

    public async Task<IReadOnlyList<PlayerRecentFormReadModel>> ListPlayerRecentFormAsync(
        Guid playerProfileId,
        int matchTake,
        CancellationToken cancellationToken = default) =>
        await (
            from assignment in dbContext.TeamAssignments
            join result in dbContext.MatchResults on new { assignment.MatchId, assignment.MatchTeamId } equals new { result.MatchId, result.MatchTeamId }
            join match in dbContext.Matches on assignment.MatchId equals match.Id
            join session in dbContext.Sessions on match.SessionId equals session.Id
            where assignment.PlayerProfileId == playerProfileId
                && (match.Status == MatchStatus.Completed || match.Status == MatchStatus.Published || match.Status == MatchStatus.Locked)
            orderby (match.CompletedAtUtc ?? session.StartsAtUtc) descending, match.MatchNumber descending
            select new PlayerRecentFormReadModel(
                match.Id,
                match.CompletedAtUtc ?? session.StartsAtUtc,
                session.TeamCount,
                result.Wins,
                result.Draws,
                result.Losses))
            .Take(matchTake)
            .ToArrayAsync(cancellationToken);

    private IQueryable<PlayerStatAggregate> BuildPlayerStatAggregates(
        Guid? seasonId,
        Guid? playerProfileId,
        Guid? groupChatId = null)
    {
        var eligibleMatches = dbContext.Matches
            .Where(match => match.Status == MatchStatus.Completed || match.Status == MatchStatus.Published || match.Status == MatchStatus.Locked)
            .Join(
                dbContext.Sessions,
                match => match.SessionId,
                session => session.Id,
                (match, session) => new { Match = match, Session = session })
            .Where(x => !seasonId.HasValue || x.Session.SeasonId == seasonId.Value);

        // Group-scoped leaderboards restrict the player set to members of the selected group chat.
        // Membership drives this filter — the underlying match facts are never group-tagged. Applied
        // as a conditional Where rather than a nullable-IQueryable predicate so it never depends on
        // the provider funcletizing a null check.
        var participants = groupChatId.HasValue
            ? dbContext.PlayerMatchStats.Where(participant => dbContext.PlayerGroupLinks
                .Any(link => link.GroupChatId == groupChatId.Value && link.PlayerProfileId == participant.PlayerProfileId))
            : dbContext.PlayerMatchStats;

        return
            from participant in participants
            join eligibleMatch in eligibleMatches on participant.MatchId equals eligibleMatch.Match.Id
            join profile in dbContext.PlayerProfiles on participant.PlayerProfileId equals profile.Id
            where participant.Played
                && (!playerProfileId.HasValue || participant.PlayerProfileId == playerProfileId.Value)
            group new { participant, profile } by new
            {
                profile.Id,
                profile.DisplayName,
                profile.PreferredPosition,
                profile.IsGuest,
                profile.IdentityUserId,
            }
            into grouped
            select new PlayerStatAggregate
            {
                PlayerProfileId = grouped.Key.Id,
                DisplayName = grouped.Key.DisplayName,
                PreferredPosition = grouped.Key.PreferredPosition,
                IsGuest = grouped.Key.IsGuest,
                IdentityUserId = grouped.Key.IdentityUserId,
                Appearances = grouped.Count(),
                MinutesPlayed = grouped.Sum(x => x.participant.MinutesPlayed ?? 0),
                Goals = dbContext.MatchEvents.Count(matchEvent =>
                    matchEvent.PlayerProfileId == grouped.Key.Id
                    && matchEvent.EventType == MatchEventType.Goal
                    && matchEvent.ReviewStatus == MatchEventReviewStatus.Approved
                    && eligibleMatches.Any(x => x.Match.Id == matchEvent.MatchId)
                    && dbContext.PlayerMatchStats.Any(x => x.MatchId == matchEvent.MatchId && x.PlayerProfileId == grouped.Key.Id && x.Played)),
                Assists = dbContext.MatchEvents.Count(matchEvent =>
                    matchEvent.AssistPlayerProfileId == grouped.Key.Id
                    && matchEvent.EventType == MatchEventType.Goal
                    && matchEvent.ReviewStatus == MatchEventReviewStatus.Approved
                    && eligibleMatches.Any(x => x.Match.Id == matchEvent.MatchId)
                    && dbContext.PlayerMatchStats.Any(x => x.MatchId == matchEvent.MatchId && x.PlayerProfileId == grouped.Key.Id && x.Played)),
                AverageRating = dbContext.PlayerRatingVotes
                    .Where(vote => vote.RatedPlayerProfileId == grouped.Key.Id && eligibleMatches.Any(x => x.Match.Id == vote.MatchId))
                    .Average(vote => (decimal?)vote.Score) ?? 0m,
                RatingVoteCount = dbContext.PlayerRatingVotes.Count(vote => vote.RatedPlayerProfileId == grouped.Key.Id && eligibleMatches.Any(x => x.Match.Id == vote.MatchId)),
                Likes = dbContext.PlayerLikes.Count(like => like.ReceiverPlayerProfileId == grouped.Key.Id && eligibleMatches.Any(x => x.Match.Id == like.MatchId)),
                MvpAwards = dbContext.MatchAwards.Count(award =>
                    award.PlayerProfileId == grouped.Key.Id
                    && award.AwardType == MatchAwardType.Mvp
                    && eligibleMatches.Any(x => x.Match.Id == award.MatchId)),
            };
    }

    private static decimal GetMetricValue(PlayerStatAggregate row, StatLeaderboardMetric metric) => metric switch
    {
        StatLeaderboardMetric.Goals => row.Goals,
        StatLeaderboardMetric.Assists => row.Assists,
        StatLeaderboardMetric.Rating => row.AverageRating,
        StatLeaderboardMetric.Mvp => row.MvpAwards,
        _ => 0m,
    };

    private sealed class PlayerStatAggregate
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
    private static async Task<int> ReassignUniqueRowsAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, bool> hasTargetDuplicate,
        Action<T> reassign)
        where T : SouthBaySoccer.Domain.Entities.Common.BaseEntity
    {
        var affected = 0;
        foreach (var row in rows)
        {
            if (hasTargetDuplicate(row))
            {
                row.IsDeleted = true;
            }
            else
            {
                reassign(row);
            }

            affected++;
        }

        await Task.CompletedTask;
        return affected;
    }
}
