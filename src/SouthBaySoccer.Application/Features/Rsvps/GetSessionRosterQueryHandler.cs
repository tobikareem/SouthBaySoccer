using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Identity;
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

        // A player who RSVP'd in-app can also arrive as an imported participant linked to the same
        // profile; the local entry wins so nobody is listed twice.
        var localProfileIds = localGoing.Select(member => member.PlayerProfileId)
            .Concat(localWaitlist.Select(member => member.PlayerProfileId))
            .ToHashSet();
        var dedupedImported = imported
            .Where(participant => participant.PlayerProfileId is not { } linkedId || !localProfileIds.Contains(linkedId))
            .ToArray();

        // Once a participant is linked, the profile is the identity: the roster shows the player's
        // registered name, not the WhatsApp handle the import captured.
        var linkedProfileIds = dedupedImported
            .Where(participant => participant.PlayerProfileId is not null)
            .Select(participant => participant.PlayerProfileId!.Value)
            .Distinct()
            .ToArray();
        var linkedProfiles = linkedProfileIds.Length == 0
            ? new Dictionary<Guid, PlayerProfile>()
            : (await playerProfileRepository.ListProfilesAsync(linkedProfileIds, cancellationToken))
                .ToDictionary(profile => profile.Id);

        var going = localGoing
            .Select(member => ToModel(member, currentProfileId))
            .Concat(dedupedImported
                .Where(participant => !participant.IsWaitlist)
                .Select(participant => ToImportedModel(participant, currentProfileId, linkedProfiles)))
            .ToArray();

        // Waitlist numbering is display-only: the local waitlist keeps its true promotion order and
        // imported participants are appended, renumbered sequentially so the roster reads 1..N. It
        // does not reflect the domain WaitlistEntry.Position used for promotion.
        var waitlistPosition = 0;
        var waitlist = localWaitlist
            .Select(member => ToModel(member, currentProfileId))
            .Concat(dedupedImported
                .Where(participant => participant.IsWaitlist)
                .Select(participant => ToImportedModel(participant, currentProfileId, linkedProfiles)))
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

    // An imported participant surfaces its linked player profile when the import resolved one;
    // unlinked participants fall back to their stable row id so multiple imported guests don't
    // collapse onto one shared key on the client. A linked row is named after its profile, falling
    // back to the imported name when that profile has none.
    private static RosterMemberModel ToImportedModel(
        PickupPalGameParticipant participant,
        Guid? currentProfileId,
        IReadOnlyDictionary<Guid, PlayerProfile> linkedProfiles)
    {
        var profile = participant.PlayerProfileId is { } profileId
            ? linkedProfiles.GetValueOrDefault(profileId)
            : null;
        return new RosterMemberModel(
            participant.PlayerProfileId ?? participant.Id,
            string.IsNullOrWhiteSpace(profile?.DisplayName) ? participant.DisplayName : profile.DisplayName,
            profile?.PreferredPosition ?? string.Empty,
            participant.IsGuest,
            IsCurrentPlayer: participant.PlayerProfileId is { } linkedId && currentProfileId == linkedId,
            WaitlistPosition: null);
    }
}
