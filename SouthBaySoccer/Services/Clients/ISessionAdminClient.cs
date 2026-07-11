using SouthBaySoccer.Contracts.Sessions;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Client boundary for the admin "Create session" flow (ADMIN-4). Provides form defaults, venue
/// search, and the create-draft / publish operations. The backend implementation gates every call on
/// <c>CanManageSessions</c>; the UI-first seed mirrors the contract against resettable seed state so
/// the client can be built and tested before the Functions endpoints exist.
/// </summary>
public interface ISessionAdminClient
{
    /// <summary>Returns the defaults used to seed the create-session form and the caller's permission.</summary>
    Task<CreateSessionDefaultsDto> GetDefaultsAsync(CancellationToken cancellationToken);

    /// <summary>Lists published sessions an admin can update.</summary>
    Task<IReadOnlyList<ManagedSessionDto>> ListManagedSessionsAsync(CancellationToken cancellationToken);

    /// <summary>Loads a managed session into the editable admin form.</summary>
    Task<ManagedSessionEditDto?> GetSessionForEditAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Searches saved and nearby venues for the given query (empty query returns all venues).</summary>
    Task<IReadOnlyList<VenueDto>> SearchVenuesAsync(string? query, CancellationToken cancellationToken);

    /// <summary>Creates a saved venue from the create-session form and returns it selected.</summary>
    Task<VenueDto> CreateVenueAsync(
        string name,
        string locality,
        string? address,
        CancellationToken cancellationToken);

    /// <summary>Creates an unpublished session draft from the entered details.</summary>
    Task<CreateSessionResult> CreateDraftAsync(CreateSessionCommand command, CancellationToken cancellationToken);

    /// <summary>Updates an existing published session and keeps it open for further updates.</summary>
    Task<CreateSessionResult> UpdateSessionAsync(
        Guid sessionId,
        CreateSessionCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a previously created draft to the team Sessions feed. Idempotent: publishing the same
    /// draft again returns the original session id without creating a duplicate.
    /// </summary>
    Task<CreateSessionResult> PublishAsync(Guid draftId, CancellationToken cancellationToken);
}

