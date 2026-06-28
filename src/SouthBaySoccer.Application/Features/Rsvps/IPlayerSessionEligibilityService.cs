namespace SouthBaySoccer.Application.Features.Rsvps;

public interface IPlayerSessionEligibilityService
{
    Task<PlayerSessionEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed record PlayerSessionEligibilityResult(bool IsEligible, string? Reason);
