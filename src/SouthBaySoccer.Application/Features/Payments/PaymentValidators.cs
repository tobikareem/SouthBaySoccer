using FluentValidation;

namespace SouthBaySoccer.Application.Features.Payments;

public sealed class CreateSessionDropInCheckoutCommandValidator : AbstractValidator<CreateSessionDropInCheckoutCommand>
{
    public CreateSessionDropInCheckoutCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.SuccessPath).NotEmpty().MaximumLength(512).Must(BeRelativePath);
        RuleFor(x => x.CancelPath).NotEmpty().MaximumLength(512).Must(BeRelativePath);
    }

    private static bool BeRelativePath(string value) =>
        Uri.TryCreate(value, UriKind.Relative, out _) && value.StartsWith("/", StringComparison.Ordinal);
}