using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>Checks the identity primary key without materializing a user or caching access.</summary>
public sealed class AccountAccessValidator(SouthBaySoccerDbContext dbContext, IClock clock)
    : IAccountAccessValidator
{
    /// <inheritdoc />
    public Task<bool> IsActiveAsync(Guid identityUserId, CancellationToken cancellationToken = default)
    {
        var now = new DateTimeOffset(clock.UtcNow);
        // Account deletion anonymizes and permanently locks this same identity in its transaction.
        return dbContext.Users.AnyAsync(user => user.Id == identityUserId &&
            (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now), cancellationToken);
    }
}
