using FluentValidation;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Application.Features.Stats;

public sealed class CreateMatchCommandValidator : AbstractValidator<CreateMatchCommand>
{
    public CreateMatchCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.MatchNumber).GreaterThan(0);
        RuleFor(x => x.Teams).NotNull().Must(x => x.Count is >= 2 and <= 4).WithMessage("A match must have two to four teams.");
        RuleForEach(x => x.Teams).ChildRules(team =>
        {
            team.RuleFor(x => x.MatchTeamId).NotEmpty();
            team.RuleFor(x => x.TeamNumber).GreaterThan(0);
            team.RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        });
        RuleFor(x => x.Teams.Select(t => t.TeamNumber)).Must(HaveUniqueValues).WithMessage("Team numbers must be unique.");
        RuleFor(x => x.Teams.Select(t => t.MatchTeamId)).Must(HaveUniqueValues).WithMessage("Team ids must be unique.");
        RuleFor(x => x.Assignments).NotEmpty();
        RuleFor(x => x.Assignments.Select(a => a.PlayerProfileId)).Must(HaveUniqueValues).WithMessage("A player can only be assigned once per match.");
        RuleForEach(x => x.Assignments).ChildRules(assignment =>
        {
            assignment.RuleFor(x => x.MatchTeamId).NotEmpty();
            assignment.RuleFor(x => x.PlayerProfileId).NotEmpty();
            assignment.RuleFor(x => x.MinutesPlayed).GreaterThanOrEqualTo(0).When(x => x.MinutesPlayed.HasValue);
            assignment.RuleFor(x => x.Position).MaximumLength(64);
        });
        RuleFor(x => x).Must(x => x.Assignments.All(a => x.Teams.Any(t => t.MatchTeamId == a.MatchTeamId))).WithMessage("Assignments must reference a match team in the same request.");
    }

    private static bool HaveUniqueValues<T>(IEnumerable<T> values) => values.Distinct().Count() == values.Count();
}

public sealed class RecordMatchEventsCommandValidator : AbstractValidator<RecordMatchEventsCommand>
{
    public RecordMatchEventsCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleForEach(x => x.Events).ChildRules(matchEvent =>
        {
            matchEvent.RuleFor(x => x.PlayerProfileId).NotEmpty();
            matchEvent.RuleFor(x => x.AssistPlayerProfileId)
                .NotEmpty()
                .When(x => x.AssistPlayerProfileId.HasValue);
            matchEvent.RuleFor(x => x.Minute).GreaterThanOrEqualTo(0);
            matchEvent.RuleFor(x => x.EventType).Must(x => x is MatchEventType.Goal or MatchEventType.OwnGoal or MatchEventType.YellowCard or MatchEventType.RedCard);
            matchEvent.RuleFor(x => x.AssistPlayerProfileId).Null().When(x => x.EventType != MatchEventType.Goal).WithMessage("Assists are only allowed on goal events.");
            matchEvent.RuleFor(x => x).Must(x => x.PlayerProfileId != x.AssistPlayerProfileId).WithMessage("A player cannot assist their own goal.");
        });
    }
}

public sealed class RecordMatchResultsCommandValidator : AbstractValidator<RecordMatchResultsCommand>
{
    public RecordMatchResultsCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Results).NotEmpty();
        RuleForEach(x => x.Results).ChildRules(result =>
        {
            result.RuleFor(x => x.MatchTeamId).NotEmpty();
            result.RuleFor(x => x.Wins).GreaterThanOrEqualTo(0);
            result.RuleFor(x => x.Draws).GreaterThanOrEqualTo(0);
            result.RuleFor(x => x.Losses).GreaterThanOrEqualTo(0);
            result.RuleFor(x => x.GoalsFor).GreaterThanOrEqualTo(0);
            result.RuleFor(x => x.GoalsAgainst).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class SubmitPeerFeedbackCommandValidator : AbstractValidator<SubmitPeerFeedbackCommand>
{
    public SubmitPeerFeedbackCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleForEach(x => x.Ratings).ChildRules(rating =>
        {
            rating.RuleFor(x => x.RatedPlayerProfileId).NotEmpty();
            rating.RuleFor(x => x.Score).InclusiveBetween(0, 10);
        });
        RuleFor(x => x.Ratings.Select(r => r.RatedPlayerProfileId)).Must(HaveUniqueValues).WithMessage("A player can only be rated once per match by the same voter.");
        RuleFor(x => x.LikedPlayerProfileIds).Must(HaveUniqueValues).WithMessage("A player can only be liked once per match by the same voter.");
    }

    private static bool HaveUniqueValues<T>(IEnumerable<T> values) => values.Distinct().Count() == values.Count();
}

public sealed class AddStatCorrectionCommandValidator : AbstractValidator<AddStatCorrectionCommand>
{
    public AddStatCorrectionCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.BeforeJson).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.AfterJson).NotEmpty().MaximumLength(4000);
    }
}


public sealed class ReviewMatchEventCommandValidator : AbstractValidator<ReviewMatchEventCommand>
{
    public ReviewMatchEventCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.MatchEventId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1024);
    }
}

public sealed class ResolveMatchReviewCommandValidator : AbstractValidator<ResolveMatchReviewCommand>
{
    public ResolveMatchReviewCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.ResolutionNote).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.BeforeJson).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.AfterJson).NotEmpty().MaximumLength(4000);
    }
}
public sealed class ReassignProfileStatsCommandValidator : AbstractValidator<ReassignProfileStatsCommand>
{
    public ReassignProfileStatsCommandValidator()
    {
        RuleFor(x => x.SourceGuestPlayerProfileId).NotEmpty();
        RuleFor(x => x.TargetPlayerProfileId).NotEmpty();
        RuleFor(x => x).Must(x => x.SourceGuestPlayerProfileId != x.TargetPlayerProfileId).WithMessage("Source and target profiles must be different.");
    }
}
public sealed class LockMatchStatsCommandValidator : AbstractValidator<LockMatchStatsCommand>
{
    public LockMatchStatsCommandValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
    }
}



public sealed class GetSeasonLeaderboardQueryValidator : AbstractValidator<GetSeasonLeaderboardQuery>
{
    public GetSeasonLeaderboardQueryValidator()
    {
        // A null season means "resolve the current season"; an explicit id must be a real one.
        RuleFor(x => x.SeasonId)
            .Must(seasonId => seasonId is null || seasonId != Guid.Empty)
            .WithMessage("'Season Id' must not be empty.");
        RuleFor(x => x.Metric).IsInEnum();
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetPlayerStatsQueryValidator : AbstractValidator<GetPlayerStatsQuery>
{
    public GetPlayerStatsQueryValidator()
    {
        RuleFor(x => x.PlayerProfileId).NotEmpty();
    }
}

public sealed class GetPlayerRecentFormQueryValidator : AbstractValidator<GetPlayerRecentFormQuery>
{
    public GetPlayerRecentFormQueryValidator()
    {
        RuleFor(x => x.PlayerProfileId).NotEmpty();
        RuleFor(x => x.Take).InclusiveBetween(1, 25);
    }
}
