using FluentValidation;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.PreferredPosition).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PhotoUri).MaximumLength(1024);
        RuleFor(x => x.EmergencyContact!.Name).NotEmpty().MaximumLength(160).When(x => x.EmergencyContact is not null);
        RuleFor(x => x.EmergencyContact!.PhoneNumber).NotEmpty().MaximumLength(40).When(x => x.EmergencyContact is not null);
        RuleFor(x => x.EmergencyContact!.Relationship).MaximumLength(80).When(x => x.EmergencyContact is not null);
    }
}
