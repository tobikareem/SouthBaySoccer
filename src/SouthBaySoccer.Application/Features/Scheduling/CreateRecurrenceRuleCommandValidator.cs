using FluentValidation;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateRecurrenceRuleCommandValidator : AbstractValidator<CreateRecurrenceRuleCommand>
{
    public CreateRecurrenceRuleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Rule).NotEmpty().MaximumLength(2048);
    }
}
