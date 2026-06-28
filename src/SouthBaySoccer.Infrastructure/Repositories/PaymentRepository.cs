using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class PaymentRepository(SouthBaySoccerDbContext dbContext) : IPaymentRepository
{
    public async Task<PaymentEligibilityProjection> GetEligibilityAsync(
        Guid playerProfileId,
        Guid sessionId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var membership = await dbContext.Memberships
            .Where(x => x.PlayerProfileId == playerProfileId && x.PaymentStatus == PaymentStatus.Active)
            .OrderByDescending(x => x.EligibleUntilUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var hasActiveMembership = membership is not null
            && (membership.EligibleUntilUtc is null || membership.EligibleUntilUtc > nowUtc);

        var hasDropIn = await dbContext.PaymentLedgerEntries.AnyAsync(
            x => x.PlayerProfileId == playerProfileId
                && x.SessionId == sessionId
                && x.EntryType == PaymentLedgerEntryType.DropIn
                && x.AmountMinor >= 0,
            cancellationToken);

        return new PaymentEligibilityProjection(
            hasActiveMembership,
            hasDropIn,
            membership?.EligibleUntilUtc);
    }

    public Task<StripeCustomerReference?> FindStripeCustomerReferenceAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        dbContext.StripeCustomerReferences.SingleOrDefaultAsync(x => x.PlayerProfileId == playerProfileId, cancellationToken);
}