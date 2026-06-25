using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

/// <summary>
/// UI-first seed implementation of <see cref="ISessionAdminClient"/> (ADMIN-4). Delegates to the
/// shared, resettable <see cref="SeedState"/> so a published session appears in the same Sessions feed
/// the dashboard and detail screens read.
/// </summary>
public sealed class SeedSessionAdminClient(SeedState state) : ISessionAdminClient
{
    public Task<CreateSessionDefaultsDto> GetDefaultsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.GetCreateSessionDefaults());
    }

    public Task<IReadOnlyList<VenueDto>> SearchVenuesAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SearchVenues(query));
    }

    public Task<CreateSessionResult> CreateDraftAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.CreateDraft(command));
    }

    public Task<CreateSessionResult> PublishAsync(Guid draftId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.Publish(draftId));
    }
}
