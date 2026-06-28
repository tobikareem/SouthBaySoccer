using FluentValidation;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateSessionOccurrenceCommandValidator : AbstractValidator<CreateSessionOccurrenceCommand>
{
    public CreateSessionOccurrenceCommandValidator()
    {
        RuleFor(x => x.RecurrenceRuleId).NotEmpty();
        RuleFor(x => x.SeasonId).NotEmpty();
        RuleFor(x => x.VenueId).NotEmpty();
        RuleFor(x => x.OccurrenceStartsAtUtc).Must(x => x.Kind == DateTimeKind.Utc).WithMessage("Occurrence start must be UTC.");
        RuleFor(x => new CreateSessionCommand(
            x.SeasonId,
            x.VenueId,
            x.Title,
            x.Format,
            x.Capacity,
            x.TeamCount,
            x.OccurrenceStartsAtUtc,
            x.CheckInOpensAtUtc,
            x.CheckInClosesAtUtc,
            x.RsvpDeadlineUtc,
            x.RecurrenceRuleId,
            "validation-key")).SetValidator(new CreateSessionCommandValidator());
    }
}
