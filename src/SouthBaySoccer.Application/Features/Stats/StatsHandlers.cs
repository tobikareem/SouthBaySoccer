using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Stats;

public sealed class CreateMatchCommandHandler(
    IValidator<CreateMatchCommand> validator,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<CreateMatchResultModel> HandleAsync(CreateMatchCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        _ = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        var match = new Match
        {
            Id = Guid.NewGuid(),
            SessionId = command.SessionId,
            MatchNumber = command.MatchNumber,
            Status = MatchStatus.Draft,
        };
        var teams = command.Teams.Select(team => new MatchTeam
        {
            Id = team.MatchTeamId,
            MatchId = match.Id,
            TeamNumber = team.TeamNumber,
            Name = team.Name.Trim(),
            CaptainPlayerProfileId = team.CaptainPlayerProfileId,
        }).ToArray();
        var assignments = command.Assignments.Select(assignment => new TeamAssignment
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            MatchTeamId = assignment.MatchTeamId,
            PlayerProfileId = assignment.PlayerProfileId,
        }).ToArray();
        var participants = command.Assignments.Select(assignment => new PlayerMatchStats
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            PlayerProfileId = assignment.PlayerProfileId,
            Played = true,
            Started = assignment.Started,
            MinutesPlayed = assignment.MinutesPlayed,
            PlayedGoalkeeper = assignment.PlayedGoalkeeper,
            Position = string.IsNullOrWhiteSpace(assignment.Position) ? null : assignment.Position.Trim(),
        }).ToArray();

        await statsRepository.CreateMatchAsync(match, teams, assignments, participants, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateMatchResultModel(
            match.Id,
            match.SessionId,
            match.MatchNumber,
            match.Status.ToString(),
            teams.Select(x => new MatchTeamModel(x.Id, x.TeamNumber, x.Name, x.CaptainPlayerProfileId)).ToArray(),
            participants.Select(x =>
            {
                var assignment = assignments.Single(a => a.PlayerProfileId == x.PlayerProfileId);
                return new TeamAssignmentModel(assignment.MatchTeamId, x.PlayerProfileId, x.Started, x.MinutesPlayed, x.PlayedGoalkeeper, x.Position);
            }).ToArray());
    }
}

public sealed class RecordMatchEventsCommandHandler(
    IValidator<RecordMatchEventsCommand> validator,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(RecordMatchEventsCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        _ = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");

        var events = command.Events.Select(matchEvent => new MatchEvent
        {
            Id = Guid.NewGuid(),
            MatchId = command.MatchId,
            PlayerProfileId = matchEvent.PlayerProfileId,
            AssistPlayerProfileId = matchEvent.AssistPlayerProfileId,
            MatchTeamId = matchEvent.MatchTeamId,
            EventType = matchEvent.EventType,
            Minute = matchEvent.Minute,
        }).ToArray();

        await statsRepository.ReplaceMatchEventsAsync(command.MatchId, events, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, events.Length);
    }
}

public sealed class RecordMatchResultsCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<RecordMatchResultsCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(RecordMatchResultsCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var match = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        var teams = await statsRepository.ListMatchTeamsAsync(command.MatchId, cancellationToken);
        var teamIds = teams.Select(x => x.Id).ToHashSet();
        if (command.Results.Count != teams.Count || command.Results.Any(x => !teamIds.Contains(x.MatchTeamId)))
        {
            throw new ApplicationConflictException("Results must include exactly one row for each match team.");
        }

        var maxOutcomeCount = teams.Count - 1;
        if (command.Results.Any(x => x.Wins + x.Draws + x.Losses > maxOutcomeCount))
        {
            throw new ApplicationConflictException("Result counters cannot exceed the number of opponents.");
        }

        var results = command.Results.Select(result => new MatchResult
        {
            Id = Guid.NewGuid(),
            MatchId = command.MatchId,
            MatchTeamId = result.MatchTeamId,
            Wins = result.Wins,
            Draws = result.Draws,
            Losses = result.Losses,
            GoalsFor = result.GoalsFor,
            GoalsAgainst = result.GoalsAgainst,
        }).ToArray();

        var existingResults = await statsRepository.ListMatchResultsAsync(command.MatchId, cancellationToken);
        if (existingResults.Count > 0 && ResultsDiffer(existingResults, results))
        {
            var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
            match.Status = MatchStatus.NeedsReview;
            await statsRepository.AddStatCorrectionAsync(new StatCorrection
            {
                Id = Guid.NewGuid(),
                MatchId = command.MatchId,
                CorrectedByPlayerProfileId = actor.Id,
                Reason = "Conflicting match result submission.",
                BeforeJson = SummarizeResults(existingResults),
                AfterJson = SummarizeResults(results),
                CorrectedAtUtc = clock.UtcNow,
            }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new StatMutationResult(command.MatchId, 1);
        }

        match.Status = MatchStatus.Completed;
        await statsRepository.UpsertMatchResultsAsync(command.MatchId, results, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, results.Length);
    }

    private static bool ResultsDiffer(IReadOnlyList<MatchResult> existingResults, IReadOnlyList<MatchResult> requestedResults) =>
        requestedResults.Any(requested =>
        {
            var existing = existingResults.SingleOrDefault(x => x.MatchTeamId == requested.MatchTeamId);
            return existing is null
                || existing.Wins != requested.Wins
                || existing.Draws != requested.Draws
                || existing.Losses != requested.Losses
                || existing.GoalsFor != requested.GoalsFor
                || existing.GoalsAgainst != requested.GoalsAgainst;
        });

    private static string SummarizeResults(IReadOnlyList<MatchResult> results) =>
        string.Join(";", results.OrderBy(x => x.MatchTeamId).Select(x => $"{x.MatchTeamId}:{x.Wins}-{x.Draws}-{x.Losses}:{x.GoalsFor}-{x.GoalsAgainst}"));
}

public sealed class SubmitPeerFeedbackCommandHandler(
    ICurrentUser currentUser,
    IValidator<SubmitPeerFeedbackCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(SubmitPeerFeedbackCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var voter = await GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        _ = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");

        if (command.Ratings.Any(x => x.RatedPlayerProfileId == voter.Id)
            || command.LikedPlayerProfileIds.Contains(voter.Id)
            || command.MvpPlayerProfileId == voter.Id)
        {
            throw new ApplicationConflictException("Players cannot rate, like, or award themselves.");
        }

        var votes = command.Ratings.Select(rating => new PlayerRatingVote
        {
            Id = Guid.NewGuid(),
            MatchId = command.MatchId,
            VoterPlayerProfileId = voter.Id,
            RatedPlayerProfileId = rating.RatedPlayerProfileId,
            Score = rating.Score,
        }).ToArray();
        var likes = command.LikedPlayerProfileIds.Select(receiverId => new PlayerLike
        {
            Id = Guid.NewGuid(),
            MatchId = command.MatchId,
            GiverPlayerProfileId = voter.Id,
            ReceiverPlayerProfileId = receiverId,
        }).ToArray();
        var award = command.MvpPlayerProfileId is null
            ? null
            : new MatchAward
            {
                Id = Guid.NewGuid(),
                MatchId = command.MatchId,
                PlayerProfileId = command.MvpPlayerProfileId.Value,
                AwardedByPlayerProfileId = voter.Id,
                AwardType = MatchAwardType.Mvp,
            };

        await statsRepository.SubmitPeerFeedbackAsync(command.MatchId, voter.Id, votes, likes, award, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, votes.Length + likes.Length + (award is null ? 0 : 1));
    }

    internal static async Task<SouthBaySoccer.Domain.Entities.Identity.PlayerProfile> GetCurrentProfileAsync(
        ICurrentUser currentUser,
        IPlayerProfileRepository playerProfileRepository,
        CancellationToken cancellationToken)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        return await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
    }
}

public sealed class AddStatCorrectionCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<AddStatCorrectionCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(AddStatCorrectionCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        _ = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");

        await statsRepository.AddStatCorrectionAsync(
            new StatCorrection
            {
                Id = Guid.NewGuid(),
                MatchId = command.MatchId,
                PlayerProfileId = command.PlayerProfileId,
                CorrectedByPlayerProfileId = actor.Id,
                Reason = command.Reason.Trim(),
                BeforeJson = command.BeforeJson,
                AfterJson = command.AfterJson,
                CorrectedAtUtc = clock.UtcNow,
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, 1);
    }
}


public sealed class ReviewMatchEventCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<ReviewMatchEventCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    private const string CanRecordStatsPolicy = "CanRecordStats";

    public async Task<StatMutationResult> HandleAsync(ReviewMatchEventCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var matchEvent = await statsRepository.FindMatchEventAsync(command.MatchEventId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match event was not found.");
        if (matchEvent.MatchId != command.MatchId)
        {
            throw new ApplicationNotFoundException("Match event was not found.");
        }

        var match = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        var teams = await statsRepository.ListMatchTeamsAsync(command.MatchId, cancellationToken);
        if (!currentUser.HasPolicy(CanRecordStatsPolicy) && !teams.Any(x => x.CaptainPlayerProfileId == actor.Id))
        {
            throw new ApplicationForbiddenException("Only assigned captains or game admins can review match events.");
        }

        var requestedStatus = command.Approved ? MatchEventReviewStatus.Approved : MatchEventReviewStatus.Rejected;
        if (matchEvent.ReviewStatus is not MatchEventReviewStatus.Pending && matchEvent.ReviewStatus != requestedStatus)
        {
            match.Status = MatchStatus.NeedsReview;
            await statsRepository.AddStatCorrectionAsync(new StatCorrection
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                PlayerProfileId = matchEvent.PlayerProfileId,
                CorrectedByPlayerProfileId = actor.Id,
                Reason = string.IsNullOrWhiteSpace(command.Note) ? "Conflicting captain stat review." : command.Note.Trim(),
                BeforeJson = $"{{\"matchEventId\":\"{matchEvent.Id}\",\"reviewStatus\":\"{matchEvent.ReviewStatus}\"}}",
                AfterJson = $"{{\"matchEventId\":\"{matchEvent.Id}\",\"reviewStatus\":\"{requestedStatus}\",\"matchStatus\":\"{MatchStatus.NeedsReview}\"}}",
                CorrectedAtUtc = clock.UtcNow,
            }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new StatMutationResult(match.Id, 1);
        }

        matchEvent.ReviewStatus = requestedStatus;
        matchEvent.ReviewedByPlayerProfileId = actor.Id;
        matchEvent.ReviewedAtUtc = clock.UtcNow;
        matchEvent.ReviewNote = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(match.Id, 1);
    }
}

public sealed class ResolveMatchReviewCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<ResolveMatchReviewCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    private const string CanRecordStatsPolicy = "CanRecordStats";

    public async Task<StatMutationResult> HandleAsync(ResolveMatchReviewCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        if (!currentUser.HasPolicy(CanRecordStatsPolicy))
        {
            throw new ApplicationForbiddenException("Only game admins can resolve match review conflicts.");
        }

        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var match = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        await statsRepository.AddStatCorrectionAsync(new StatCorrection
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            CorrectedByPlayerProfileId = actor.Id,
            Reason = command.ResolutionNote.Trim(),
            BeforeJson = command.BeforeJson,
            AfterJson = command.AfterJson,
            CorrectedAtUtc = clock.UtcNow,
        }, cancellationToken);

        match.Status = MatchStatus.Completed;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(match.Id, 1);
    }
}
public sealed class ReassignProfileStatsCommandHandler(
    IClock clock,
    IValidator<ReassignProfileStatsCommand> validator,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(ReassignProfileStatsCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var affected = await statsRepository.ReassignProfileStatsAsync(
            command.SourceGuestPlayerProfileId,
            command.TargetPlayerProfileId,
            cancellationToken);
        await statsRepository.AddProfileStatReassignmentAuditAsync(new ProfileStatReassignmentAudit
        {
            Id = Guid.NewGuid(),
            SourceGuestPlayerProfileId = command.SourceGuestPlayerProfileId,
            TargetPlayerProfileId = command.TargetPlayerProfileId,
            AffectedCount = affected,
            ReassignedAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(Guid.Empty, affected);
    }
}
public sealed class LockMatchStatsCommandHandler(
    IValidator<LockMatchStatsCommand> validator,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(LockMatchStatsCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var match = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");

        match.Status = MatchStatus.Locked;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, 1);
    }
}











public sealed class GetSeasonLeaderboardQueryHandler(
    IValidator<GetSeasonLeaderboardQuery> validator,
    ISeasonRepository seasonRepository,
    IStatsRepository statsRepository)
{
    public async Task<LeaderboardModel> HandleAsync(GetSeasonLeaderboardQuery query, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        var season = await seasonRepository.GetByIdAsync(query.SeasonId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Season was not found.");

        var skip = (query.Page - 1) * query.PageSize;
        var rows = await statsRepository.ListSeasonLeaderboardAsync(
            query.SeasonId,
            query.Metric,
            skip,
            query.PageSize,
            cancellationToken);

        return new LeaderboardModel(
            season.Id,
            season.Name,
            query.Metric,
            GetLeaderboardNote(query.Metric),
            rows.Select((row, index) => new LeaderboardRowModel(
                skip + index + 1,
                ToPlayerSummary(row.PlayerProfileId, row.DisplayName, row.PreferredPosition, row.IsGuest, row.IdentityUserId),
                row.Appearances,
                row.Value)).ToArray());
    }

    private static string GetLeaderboardNote(StatLeaderboardMetric metric) => metric switch
    {
        StatLeaderboardMetric.Goals => "Only approved goals count. Ties break by fewer appearances, then assists.",
        StatLeaderboardMetric.Assists => "Only assists on approved goals count. Ties break by fewer minutes when available, then goals.",
        StatLeaderboardMetric.Rating => "Average rating uses votes received on eligible matches; zero-vote matches are excluded.",
        StatLeaderboardMetric.Mvp => "MVP rank uses explicit MVP awards. Ties break by fewer appearances, then rating.",
        _ => "Leaderboard values are derived from approved raw match facts.",
    };

    internal static PlayerSummaryModel ToPlayerSummary(
        Guid playerProfileId,
        string displayName,
        string preferredPosition,
        bool isGuest,
        Guid? identityUserId) =>
        new(playerProfileId, displayName, BuildInitials(displayName), preferredPosition, isGuest, identityUserId);

    private static string BuildInitials(string displayName)
    {
        var initials = string.Concat(displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));
        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }
}

public sealed class GetPlayerStatsQueryHandler(
    IValidator<GetPlayerStatsQuery> validator,
    IStatsRepository statsRepository)
{
    public async Task<PlayerStatsModel> HandleAsync(GetPlayerStatsQuery query, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        var stats = await statsRepository.GetPlayerStatsAsync(query.PlayerProfileId, query.SeasonId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        return new PlayerStatsModel(
            stats.PlayerProfileId,
            query.SeasonId,
            stats.Appearances,
            stats.Goals,
            stats.Assists,
            stats.AverageRating,
            stats.MvpAwards,
            stats.Likes);
    }
}

public sealed class GetMyPlayerStatsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    GetPlayerStatsQueryHandler getPlayerStatsHandler)
{
    public async Task<PlayerStatsModel> HandleAsync(GetMyPlayerStatsQuery query, CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        return await getPlayerStatsHandler.HandleAsync(new GetPlayerStatsQuery(profile.Id, query.SeasonId), cancellationToken);
    }
}

public sealed class GetPlayerRecentFormQueryHandler(
    IValidator<GetPlayerRecentFormQuery> validator,
    IStatsRepository statsRepository)
{
    public async Task<RecentFormModel> HandleAsync(GetPlayerRecentFormQuery query, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        var rows = await statsRepository.ListPlayerRecentFormAsync(query.PlayerProfileId, query.Take, cancellationToken);
        var outcomes = new List<RecentFormOutcome>(query.Take);

        foreach (var row in rows)
        {
            if (row.Wins + row.Draws + row.Losses > row.TeamCount - 1)
            {
                throw new ApplicationConflictException("Stored match result counters exceed the session team count.");
            }

            outcomes.AddRange(Enumerable.Repeat(RecentFormOutcome.Win, row.Wins));
            outcomes.AddRange(Enumerable.Repeat(RecentFormOutcome.Draw, row.Draws));
            outcomes.AddRange(Enumerable.Repeat(RecentFormOutcome.Loss, row.Losses));
            if (outcomes.Count >= query.Take)
            {
                break;
            }
        }

        return new RecentFormModel(query.PlayerProfileId, outcomes.Take(query.Take).ToArray());
    }
}
