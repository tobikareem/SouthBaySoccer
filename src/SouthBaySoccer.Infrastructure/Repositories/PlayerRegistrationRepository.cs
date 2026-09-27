using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class PlayerRegistrationRepository(SouthBaySoccerDbContext dbContext) : IPlayerRegistrationRepository
{
    public Task<PlayerRegistration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PlayerRegistrations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PlayerRegistration?> FindAwaitingExternalByPhoneNumberHashAsync(
        string phoneNumberHash,
        CancellationToken cancellationToken = default) =>
        dbContext.PlayerRegistrations
            .Where(x => x.PhoneNumberHash == phoneNumberHash &&
                (x.Status == PlayerRegistrationStatus.PendingExternal || x.Status == PlayerRegistrationStatus.ExternalFailed))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(PlayerRegistration entity, CancellationToken cancellationToken = default) =>
        await dbContext.PlayerRegistrations.AddAsync(entity, cancellationToken);

    public void Update(PlayerRegistration entity) => dbContext.PlayerRegistrations.Update(entity);

    public void SoftDelete(PlayerRegistration entity) => dbContext.PlayerRegistrations.Remove(entity);
}
