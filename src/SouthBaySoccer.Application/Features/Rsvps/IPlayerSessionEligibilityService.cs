namespace SouthBaySoccer.Application.Features.Rsvps;

public interface IPlayerSessionEligibilityService
{
    Task<PlayerSessionEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates eligibility for several players using batched compliance reads. Callers use this
    /// to resolve waitlist promotion candidates before opening a serializable transaction, so the
    /// transaction never awaits a per-candidate compliance query while holding range locks.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, bool>> CheckManyAsync(
        IReadOnlyCollection<Guid> playerProfileIds,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed record PlayerSessionEligibilityResult(bool IsEligible, string? Reason);
