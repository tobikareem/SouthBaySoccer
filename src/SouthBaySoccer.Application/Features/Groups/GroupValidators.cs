using FluentValidation;

namespace SouthBaySoccer.Application.Features.Groups;

public sealed class LinkPlayerToGroupCommandValidator : AbstractValidator<LinkPlayerToGroupCommand>
{
    public LinkPlayerToGroupCommandValidator()
    {
        RuleFor(x => x.GroupExternalId).NotEmpty().MaximumLength(256);
    }
}
