using System.Linq;
using FluentValidation;

namespace SouthBaySoccer.Application.Features.Groups;

public sealed class LinkPlayerToGroupCommandValidator : AbstractValidator<LinkPlayerToGroupCommand>
{
    public LinkPlayerToGroupCommandValidator()
    {
        RuleFor(x => x.GroupExternalId).NotEmpty().MaximumLength(256);
    }
}

public sealed class RequestGroupMembershipsCommandValidator : AbstractValidator<RequestGroupMembershipsCommand>
{
    public const int MaxGroups = 50;

    public RequestGroupMembershipsCommandValidator()
    {
        RuleFor(x => x.GroupChatIds).NotEmpty();
        RuleFor(x => x.GroupChatIds.Count).LessThanOrEqualTo(MaxGroups);
        RuleForEach(x => x.GroupChatIds).NotEmpty();
    }
}

/// <summary>
/// The search term is a display-name fragment only. Anything that looks like a phone number or an
/// email is refused so personal identifiers never travel in a query string (they would otherwise
/// land in request logs).
/// </summary>
public sealed class SearchPlayersQueryValidator : AbstractValidator<SearchPlayersQuery>
{
    public const int MinLength = 2;
    public const int MaxLength = 64;

    public SearchPlayersQueryValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty()
            .Must(query => query.Trim().Length >= MinLength).WithMessage($"Enter at least {MinLength} characters.")
            .MaximumLength(MaxLength)
            .Must(query => !LooksLikePersonalIdentifier(query)).WithMessage("Search by name only.");
    }

    internal static bool LooksLikePersonalIdentifier(string query) =>
        query.Contains('@') || query.Count(char.IsDigit) >= 4;
}
