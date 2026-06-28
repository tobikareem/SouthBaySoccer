using FluentValidation;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(x => x.SeasonId).NotEmpty();
        RuleFor(x => x.VenueId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Format).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x.TeamCount).InclusiveBetween(2, 4);
        RuleFor(x => x.StartsAtUtc).Must(BeUtc).WithMessage("Start must be UTC.");
        RuleFor(x => x.CheckInOpensAtUtc).Must(BeUtc).WithMessage("Check-in open must be UTC.");
        RuleFor(x => x.CheckInClosesAtUtc).Must(BeUtc).WithMessage("Check-in close must be UTC.");
        RuleFor(x => x.RsvpDeadlineUtc).Must(BeUtc).WithMessage("RSVP deadline must be UTC.");
        RuleFor(x => x).Must(x => x.CheckInOpensAtUtc < x.CheckInClosesAtUtc).WithMessage("Check-in open must be before check-in close.");
        RuleFor(x => x).Must(x => x.CheckInClosesAtUtc <= x.StartsAtUtc).WithMessage("Check-in close must be before or at session start.");
        RuleFor(x => x).Must(x => x.RsvpDeadlineUtc < x.StartsAtUtc).WithMessage("RSVP deadline must be before session start.");
        RuleFor(x => x.OccurrenceKey).MaximumLength(256);
    }

    private static bool BeUtc(DateTime value) => value.Kind == DateTimeKind.Utc;
}
