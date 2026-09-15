using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class PendingPhoneSignInRepository(SouthBaySoccerDbContext dbContext) : IPendingPhoneSignInRepository
{
    public async Task AddAsync(PendingPhoneSignIn pendingSignIn, CancellationToken cancellationToken = default) =>
        await dbContext.PendingPhoneSignIns.AddAsync(pendingSignIn, cancellationToken);

    public void Update(PendingPhoneSignIn pendingSignIn) => dbContext.PendingPhoneSignIns.Update(pendingSignIn);

    public Task<PendingPhoneSignIn?> FindActiveByPickupPalUserIdAsync(
        string pickupPalUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.PendingPhoneSignIns
            .Where(x => x.PickupPalUserId == pickupPalUserId && x.ConsumedAtUtc == null && x.ExpiresAtUtc > nowUtc)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
