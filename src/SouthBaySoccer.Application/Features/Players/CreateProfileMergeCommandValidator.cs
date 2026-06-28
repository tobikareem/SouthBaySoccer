using FluentValidation;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class CreateProfileMergeCommandValidator : AbstractValidator<CreateProfileMergeCommand>
{
    public CreateProfileMergeCommandValidator()
    {
        RuleFor(x => x.SourceGuestPlayerProfileId).NotEmpty();
        RuleFor(x => x.TargetPlayerProfileId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.SourceGuestPlayerProfileId != x.TargetPlayerProfileId)
            .WithMessage("Source and target player profiles must be different.");
    }
}
