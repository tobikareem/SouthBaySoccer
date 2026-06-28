using FluentValidation;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateSeasonCommandValidator : AbstractValidator<CreateSeasonCommand>
{
    public CreateSeasonCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.StartsAtUtc).Must(x => x.Kind == DateTimeKind.Utc).WithMessage("Start must be UTC.");
        RuleFor(x => x.EndsAtUtc).Must(x => x.Kind == DateTimeKind.Utc).WithMessage("End must be UTC.");
        RuleFor(x => x).Must(x => x.EndsAtUtc > x.StartsAtUtc).WithMessage("Season end must be after start.");
    }
}
