using System.Text.Json;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record ClaimableParticipantModel(Guid ParticipantId, string DisplayName, bool IsWaitlist);

public sealed record SessionClaimablesModel(
    Guid SessionId,
    string MyRegisteredName,
    bool AlreadyOnRoster,
    IReadOnlyList<ClaimableParticipantModel> Claimable);

public sealed record ClaimableSessionModel(
    Guid SessionId,
    string Title,
    DateTime StartsAtUtc,
    int ClaimableCount);

public sealed record GetSessionClaimablesQuery(Guid SessionId);

public sealed record GetSessionUnlinkedParticipantsQuery(Guid SessionId);

public sealed record GetMyClaimableSessionsQuery;

public sealed record ClaimParticipantCommand(Guid SessionId, Guid ParticipantId);

/// <summary>
/// Shared logic for the self-claim flow: a signed-in player attaches themselves to an imported
/// Pickup Pal participant that never linked to a profile (guests, or a WhatsApp name that matched
/// nothing at import). Because the two identity systems share no reliable key, the person confirms
/// their own row - within a single roster display names are unique, so the choice is unambiguous.
/// </summary>
internal static class MatchClaimQueries
{
    internal static bool IsOnRoster(
        IReadOnlyList<PickupPalGameParticipant> participants,
        IReadOnlyList<RosterMemberRecord> localGoing,
        IReadOnlyList<RosterMemberRecord> localWaitlist,
        Guid profileId) =>
        participants.Any(p => p.PlayerProfileId == profileId)
        || localGoing.Any(m => m.PlayerProfileId == profileId)
        || localWaitlist.Any(m => m.PlayerProfileId == profileId);
}

public sealed class GetSessionClaimablesQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository)
{
    public async Task<SessionClaimablesModel> HandleAsync(
        GetSessionClaimablesQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        _ = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, query.SessionId, cancellationToken);

        var participants = await pickupPalGameRepository.ListParticipantsAsync(query.SessionId, cancellationToken);
        var going = await rsvpRepository.ListGoingRosterAsync(query.SessionId, cancellationToken);
        var waitlist = await rsvpRepository.ListActiveWaitlistRosterAsync(query.SessionId, cancellationToken);
        var alreadyOnRoster = MatchClaimQueries.IsOnRoster(participants, going, waitlist, actor.Id);

        // Unclaimed = snapshot-only rows with no linked profile at all. Someone already resolved to a
        // profile (guest or real) is out of scope for a plain self-claim.
        var claimable = alreadyOnRoster
            ? []
            : participants
                .Where(p => p.PlayerProfileId is null)
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new ClaimableParticipantModel(p.Id, p.DisplayName, p.IsWaitlist))
                .ToArray();

        return new SessionClaimablesModel(query.SessionId, actor.DisplayName, alreadyOnRoster, claimable);
    }
}

/// <summary>
/// A game's unclaimed imported entries, for a game admin to match to real profiles - unconditional,
/// unlike the self-claim list which hides itself once the caller is on the roster.
/// </summary>
public sealed class GetSessionUnlinkedParticipantsQueryHandler(
    ICurrentUser currentUser,
    ISessionRepository sessionRepository,
    IPickupPalGameRepository pickupPalGameRepository)
{
    public async Task<IReadOnlyList<ClaimableParticipantModel>> HandleAsync(
        GetSessionUnlinkedParticipantsQuery query,
        CancellationToken cancellationToken = default)
    {
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        _ = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, query.SessionId, cancellationToken);
        var participants = await pickupPalGameRepository.ListParticipantsAsync(query.SessionId, cancellationToken);
        return participants
            .Where(p => p.PlayerProfileId is null)
            .OrderBy(p => p.DisplayOrder)
            .Select(p => new ClaimableParticipantModel(p.Id, p.DisplayName, p.IsWaitlist))
            .ToArray();
    }
}

/// <summary>
/// Recent sessions a signed-in player is not on but that still have unclaimed entries, so onboarding
/// can prompt "did you play in these? claim your spot".
/// </summary>
public sealed class GetMyClaimableSessionsQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository)
{
    /// <summary>How far back a player can still claim a spot on a game they played.</summary>
    public static readonly TimeSpan ClaimWindow = TimeSpan.FromDays(3);

    public async Task<IReadOnlyList<ClaimableSessionModel>> HandleAsync(
        GetMyClaimableSessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var nowUtc = clock.UtcNow;
        var sessions = await sessionRepository.ListGameDayCandidatesAsync(
            nowUtc.Subtract(ClaimWindow),
            nowUtc,
            cancellationToken);

        var results = new List<ClaimableSessionModel>(sessions.Count);
        foreach (var session in sessions.OrderByDescending(x => x.StartsAtUtc))
        {
            var participants = await pickupPalGameRepository.ListParticipantsAsync(session.Id, cancellationToken);
            var going = await rsvpRepository.ListGoingRosterAsync(session.Id, cancellationToken);
            var waitlist = await rsvpRepository.ListActiveWaitlistRosterAsync(session.Id, cancellationToken);
            if (MatchClaimQueries.IsOnRoster(participants, going, waitlist, actor.Id))
            {
                continue;
            }

            var count = participants.Count(p => p.PlayerProfileId is null);
            if (count > 0)
            {
                results.Add(new ClaimableSessionModel(session.Id, session.Title, session.StartsAtUtc, count));
            }
        }

        return results;
    }
}

/// <summary>
/// A signed-in player claims an unclaimed participant row as themselves. The command never takes a
/// target profile - it always links to the caller - so a player can only ever claim a spot for
/// themselves. Guarded to unclaimed rows and one claim per session; every claim is audited so a bad
/// claim can be traced and re-linked by an admin.
/// </summary>
public sealed class ClaimParticipantCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        ClaimParticipantCommand command,
        CancellationToken cancellationToken = default)
    {
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        _ = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        var participant = await pickupPalGameRepository.FindParticipantAsync(command.ParticipantId, cancellationToken)
            ?? throw new ApplicationNotFoundException("That player entry was not found.");
        if (participant.SessionId != command.SessionId)
        {
            throw new ApplicationNotFoundException("That player entry was not found.");
        }

        if (participant.PlayerProfileId is not null)
        {
            throw new ApplicationConflictException("That spot has already been claimed.");
        }

        var participants = await pickupPalGameRepository.ListParticipantsAsync(command.SessionId, cancellationToken);
        var going = await rsvpRepository.ListGoingRosterAsync(command.SessionId, cancellationToken);
        var waitlist = await rsvpRepository.ListActiveWaitlistRosterAsync(command.SessionId, cancellationToken);
        if (MatchClaimQueries.IsOnRoster(participants, going, waitlist, actor.Id))
        {
            throw new ApplicationConflictException("You already have a spot on this game.");
        }

        participant.PlayerProfileId = actor.Id;
        pickupPalGameRepository.UpdateParticipant(participant);
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "PickupPalParticipant.SelfClaim",
            EntityName = nameof(PickupPalGameParticipant),
            EntityId = participant.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = participant.SessionId,
                participantId = participant.Id,
                claimedDisplayName = participant.DisplayName,
                playerProfileId = actor.Id,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GameDayMutationModel(participant.SessionId, Guid.Empty, 1);
    }
}
