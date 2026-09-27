namespace SouthBaySoccer.Application.Abstractions.Authentication;

/// <summary>Checks whether a token subject still has access to the local account.</summary>
public interface IAccountAccessValidator
{
    /// <summary>Checks current persisted account state; successful results must not be cached.</summary>
    Task<bool> IsActiveAsync(Guid identityUserId, CancellationToken cancellationToken = default);
}
