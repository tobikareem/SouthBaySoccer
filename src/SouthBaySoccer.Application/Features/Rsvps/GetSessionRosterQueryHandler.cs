using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

/// <summary>One entry on a session roster, from a local profile or an imported participant.</summary>
public sealed record RosterMemberModel(
    Guid? PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    bool IsCurrentPlayer,
    int? WaitlistPosition);

/// <summary>A session's going list and ordered waitlist.</summary>
public sealed record SessionRosterModel(
    Guid SessionId,
    IReadOnlyList<RosterMemberModel> Going,
    IReadOnlyList<RosterMemberModel> Waitlist);

/// <summary>
/// Reads a session's roster: local RSVP/waitlist rows (player profiles) unioned with imported
/// Pickup Pal participants. Local players lead each list; imported participants follow in their
/// Pickup Pal join order, with waitlist positions numbered after the local waitlist.
/// </summary>
public sealed class GetSessionRosterQueryHandler(
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository gameRepository,
    IPlayerProfileRepository playerProfileRepository,
    ICurrentUser currentUser)
{
    public async Task<SessionRosterModel> HandleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        var currentProfileId = await FindCurrentProfileIdAsync(cancellationToken);
        var localGoing = await rsvpRepository.ListGoingRosterAsync(session.Id, cancellationToken);
        var localWaitlist = await rsvpRepository.ListActiveWaitlistRosterAsync(session.Id, cancellationToken);
        var imported = await gameRepository.ListParticipantsAsync(session.Id, cancellationToken);

        var going = localGoing
            .Select(member => ToModel(member, currentProfileId))
            .Concat(imported
                .Where(participant => !participant.IsWaitlist)
                .Select(ToImportedModel))
            .ToArray();

        // Waitlist numbering is display-only: the local waitlist keeps its true promotion order and
        // imported participants are appended, renumbered sequentially so the roster reads 1..N. It
        // does not reflect the domain WaitlistEntry.Position used for promotion.
        var waitlistPosition = 0;
        var waitlist = localWaitlist
            .Select(member => ToModel(member, currentProfileId))
            .Concat(imported
                .Where(participant => participant.IsWaitlist)
                .Select(ToImportedModel))
            .Select(member => member with { WaitlistPosition = ++waitlistPosition })
            .ToArray();

        return new SessionRosterModel(session.Id, going, waitlist);
    }

    private async Task<Guid?> FindCurrentProfileIdAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(userId, cancellationToken);
        return profile?.Id;
    }

    private static RosterMemberModel ToModel(RosterMemberRecord member, Guid? currentProfileId) =>
        new(
            member.PlayerProfileId,
            member.DisplayName,
            member.PreferredPosition,
            member.IsGuest,
            IsCurrentPlayer: currentProfileId == member.PlayerProfileId,
            member.WaitlistPosition);

    // Imported participants have no player profile. Their stable row id is surfaced as the roster
    // entry id so multiple imported guests don't collapse onto one shared key on the client.
    private static RosterMemberModel ToImportedModel(PickupPalGameParticipant participant) =>
        new(
            participant.Id,
            participant.DisplayName,
            string.Empty,
            participant.IsGuest,
            IsCurrentPlayer: false,
            WaitlistPosition: null);
}
