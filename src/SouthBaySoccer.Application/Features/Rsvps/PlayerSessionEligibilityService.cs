namespace SouthBaySoccer.Application.Features.Rsvps;

/// <summary>
/// Decides whether a player may RSVP, be promoted from the waitlist, or check in for a session.
/// Product decision (2026-09-16): there is no waiver requirement any more, so eligibility is the
/// payment verdict alone. The Compliance entities and the <c>waivers/*</c> endpoints stay in place
/// (dormant) and are no longer consulted here.
/// </summary>
public sealed class PlayerSessionEligibilityService(
    IPaymentEligibilityService paymentEligibilityService) : IPlayerSessionEligibilityService
{
    public async Task<PlayerSessionEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var payment = await paymentEligibilityService.CheckAsync(playerProfileId, sessionId, cancellationToken);
        return payment.IsEligible
            ? new PlayerSessionEligibilityResult(true, null)
            : new PlayerSessionEligibilityResult(false, payment.Reason ?? "Payment required.");
    }

    public async Task<IReadOnlyDictionary<Guid, bool>> CheckManyAsync(
        IReadOnlyCollection<Guid> playerProfileIds,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<Guid, bool>(playerProfileIds.Count);
        foreach (var playerProfileId in playerProfileIds.Distinct())
        {
            results[playerProfileId] =
                (await paymentEligibilityService.CheckAsync(playerProfileId, sessionId, cancellationToken)).IsEligible;
        }

        return results;
    }
}
