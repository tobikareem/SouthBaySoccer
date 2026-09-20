using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

/// <summary>
/// Decides whether a player may RSVP, be promoted from the waitlist, or check in for a session.
/// Product decision (2026-09-16): there is no waiver requirement any more, so eligibility is the
/// payment verdict, with current group membership also checked for waitlist promotion.
/// Interactive join/check-in handlers enforce membership separately so withdrawal stays available.
/// The Compliance entities and the <c>waivers/*</c> endpoints stay in place
/// (dormant) and are no longer consulted here.
/// </summary>
public sealed class PlayerSessionEligibilityService(
    IPaymentEligibilityService paymentEligibilityService,
    ISessionRepository sessionRepository,
    IPlayerGroupLinkRepository membershipRepository) : IPlayerSessionEligibilityService
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
        var candidates = playerProfileIds.Distinct().ToArray();
        var results = candidates.ToDictionary(id => id, _ => false);
        if (candidates.Length == 0)
        {
            return results;
        }

        // Called inside the serializable cancellation transaction: do not reuse a membership
        // verdict from when the player originally joined the waitlist.
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || GroupMembershipGate.HasUnresolvedImportedGroup(session))
        {
            return results;
        }

        var approved = session.GroupChatId is { } groupId
            ? (await membershipRepository.ListApprovedPlayerIdsAsync(groupId, candidates, cancellationToken)).ToHashSet()
            : candidates.ToHashSet();
        foreach (var playerProfileId in candidates.Where(approved.Contains))
        {
            results[playerProfileId] =
                (await paymentEligibilityService.CheckAsync(playerProfileId, sessionId, cancellationToken)).IsEligible;
        }

        return results;
    }
}
