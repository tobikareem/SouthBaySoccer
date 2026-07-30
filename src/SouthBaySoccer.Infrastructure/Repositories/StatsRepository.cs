using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Common;
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

        // Captaincy must follow the merge with the assignments: leaving MatchTeam pointing at the
        // retired source profile orphans the team — the captain checkbox can no longer preselect,
        // and CanLockTeams fails its captain-has-assignment check, hiding the Lock button entirely.
        var sourceCaptainTeams = await dbContext.MatchTeams
            .Where(x => x.CaptainPlayerProfileId == sourcePlayerProfileId)
            .ToArrayAsync(cancellationToken);
        var captainMatchIds = sourceCaptainTeams.Select(x => x.MatchId).Distinct().ToArray();
        var targetCaptainTeams = await dbContext.MatchTeams
            .Where(x => captainMatchIds.Contains(x.MatchId)
                && x.CaptainPlayerProfileId == targetPlayerProfileId)
            .ToArrayAsync(cancellationToken);
        if (sourceCaptainTeams.GroupBy(x => x.MatchId).Any(group => group.Count() > 1)
            || targetCaptainTeams.Any(targetTeam => sourceCaptainTeams.Any(
                sourceTeam => sourceTeam.MatchId == targetTeam.MatchId
                    && sourceTeam.Id != targetTeam.Id)))
        {
            throw new ApplicationConflictException(
                "These profiles captain different teams in the same match. Resolve the captain assignments before matching the players.");
        }

        var sourceCaptainTeamIds = sourceCaptainTeams.Select(x => x.Id).ToHashSet();
        foreach (var team in sourceCaptainTeams)
        {
            team.CaptainPlayerProfileId = targetPlayerProfileId;
            affected++;
        }

        var sourceAssignments = await dbContext.TeamAssignments
            .Where(x => x.PlayerProfileId == sourcePlayerProfileId)
            .ToArrayAsync(cancellationToken);
        var assignmentMatchIds = sourceAssignments.Select(x => x.MatchId).Distinct().ToArray();
        var targetAssignmentsByMatchId = (await dbContext.TeamAssignments
                .Where(x => assignmentMatchIds.Contains(x.MatchId)
                    && x.PlayerProfileId == targetPlayerProfileId)
                .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.MatchId);
        foreach (var sourceAssignment in sourceAssignments)
        {
            if (!targetAssignmentsByMatchId.TryGetValue(sourceAssignment.MatchId, out var targetAssignment))
            {
                sourceAssignment.PlayerProfileId = targetPlayerProfileId;
                affected++;
                continue;
            }

            if (sourceCaptainTeamIds.Contains(sourceAssignment.MatchTeamId)
                && targetAssignment.MatchTeamId != sourceAssignment.MatchTeamId)
            {
                // A merged captain must remain assigned to the team they captain. If the canonical
                // profile was already drafted elsewhere, move its existing assignment onto the
                // captain's team and retire the duplicate row. Keeping the canonical row avoids a
                // transient duplicate-key violation when EF persists both changes in one unit.
                targetAssignment.MatchTeamId = sourceAssignment.MatchTeamId;
                sourceAssignment.IsDeleted = true;
                affected += 2;
                continue;
            }

            sourceAssignment.IsDeleted = true;
            affected++;
        }

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
        var aggregates = await ListPlayerStatAggregatesAsync(
            seasonId,
            playerProfileId: null,
            groupChatId,
            cancellationToken);

        // Ordering and paging happen in memory. The ranking keys are all derived aggregates, so the
        // old SQL ORDER BY had to compute every player's totals before it could page anyway — the
        // page never reduced the work. A pickup community is hundreds of players, so sorting the
        // materialized set costs nothing and keeps the ordering contract in one testable place.
        var rows = LeaderboardProjection.Order(aggregates, metric).Skip(skip).Take(take);
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
            LeaderboardProjection.GetMetricValue(row, metric))).ToArray();
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

        var aggregate = (await ListPlayerStatAggregatesAsync(
                seasonId,
                playerProfileId,
                groupChatId: null,
                cancellationToken))
            .SingleOrDefault();
        // Career wins/losses are game-level: sum the player's team results across finalized matches
        // (a session's rotation can be several games, so this matches the recent-form outcomes).
        var record = await (
                from assignment in dbContext.TeamAssignments
                join result in dbContext.MatchResults
                    on new { assignment.MatchId, assignment.MatchTeamId } equals new { result.MatchId, result.MatchTeamId }
                join match in dbContext.Matches on assignment.MatchId equals match.Id
                where assignment.PlayerProfileId == playerProfileId
                    && (match.Status == MatchStatus.Completed
                        || match.Status == MatchStatus.Published
                        || match.Status == MatchStatus.Locked)
                select result)
            .GroupBy(_ => 1)
            .Select(g => new { Wins = g.Sum(x => x.Wins), Losses = g.Sum(x => x.Losses) })
            .SingleOrDefaultAsync(cancellationToken);
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
            aggregate?.MvpAwards ?? 0,
            record?.Wins ?? 0,
            record?.Losses ?? 0);
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

    /// <summary>
    /// Assembles per-player season aggregates from raw match facts using one flat grouped query per
    /// fact type. The previous shape ran six correlated subqueries per grouped player, each
    /// re-inlining the eligible-match join, which made cost scale with players x facts.
    /// </summary>
    private async Task<IReadOnlyList<PlayerStatAggregate>> ListPlayerStatAggregatesAsync(
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
                .Any(link => link.GroupChatId == groupChatId.Value && link.PlayerProfileId == participant.PlayerProfileId))
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
