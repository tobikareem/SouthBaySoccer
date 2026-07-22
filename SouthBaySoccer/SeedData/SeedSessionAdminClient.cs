using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Contracts.Common;
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

    public Task<IReadOnlyList<ManagedSessionDto>> ListManagedSessionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.ListManagedSessions());
    }

    public Task<ManagedSessionEditDto?> GetSessionForEditAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.GetSessionForEdit(sessionId));
    }

    public Task<IReadOnlyList<VenueDto>> SearchVenuesAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SearchVenues(query));
    }

    public Task<VenueDto> CreateVenueAsync(
        string name,
        string locality,
        string? address,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.CreateVenue(name, locality, address));
    }

    public Task<CreateSessionResult> CreateDraftAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.CreateDraft(command));
    }

    public Task<CreateSessionResult> UpdateSessionAsync(
        Guid sessionId,
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.UpdateSession(sessionId, command));
    }

    public Task<CreateSessionResult> PublishAsync(Guid draftId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.Publish(draftId));
    }

    public Task<ClientCommandResult> CancelSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.CancelSession(sessionId));
    }

    public Task<ClientCommandResult> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.DeleteSession(sessionId));
    }
}
