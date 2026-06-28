using FluentValidation;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Application.Features.Rsvps;

public sealed class SubmitRsvpCommandValidator : AbstractValidator<SubmitRsvpCommand>
{
    public SubmitRsvpCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Status).Must(x => x is RsvpStatus.Going or RsvpStatus.Maybe or RsvpStatus.NotGoing);
    }
}

public sealed class AdminOverrideRsvpCommandValidator : AbstractValidator<AdminOverrideRsvpCommand>
{
    public AdminOverrideRsvpCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.PlayerProfileId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1024);
    }
}

public sealed class CheckInPlayerCommandValidator : AbstractValidator<CheckInPlayerCommand>
{
    public CheckInPlayerCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.PlayerProfileId).NotEmpty();
        RuleFor(x => x.Outcome).Must(x => x is AttendanceOutcome.CheckedIn or AttendanceOutcome.Late or AttendanceOutcome.NoShow or AttendanceOutcome.Excused);
        RuleFor(x => x.LateOverrideReason).MaximumLength(1024);
    }
}
