using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.UserActivity;

public sealed record GetUserActivityQuery(int Page = 1, int PageSize = 25);

public sealed class GetUserActivityQueryValidator : AbstractValidator<GetUserActivityQuery>
{
    public GetUserActivityQueryValidator()
    {
        RuleFor(query => query.Page).InclusiveBetween(1, 10000);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetUserActivityQueryHandler(ICurrentUser currentUser, IUserActivityRepository repository)
{
    public async Task<UserActivityPageReadModel> HandleAsync(GetUserActivityQuery query, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is null)
        {
            throw new ApplicationUnauthenticatedException();
        }
        if (!GroupMembershipAuthorization.IsSuperAdmin(currentUser))
        {
            throw new ApplicationForbiddenException("Only the owner can view user activity.");
        }
        await new GetUserActivityQueryValidator().ValidateAndThrowAsync(query, cancellationToken);
        return await repository.ReadPageAsync(query.Page, query.PageSize, cancellationToken);
    }
}
