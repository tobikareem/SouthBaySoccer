using FluentValidation;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateVenueCommandValidator : AbstractValidator<CreateVenueCommand>
{
    public CreateVenueCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Locality).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Address).MaximumLength(512);
    }
}
