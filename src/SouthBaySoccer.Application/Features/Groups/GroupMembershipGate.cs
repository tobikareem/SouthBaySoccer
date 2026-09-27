using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Role-based standing that group handlers evaluate themselves, since policies are global.</summary>
public static class GroupMembershipAuthorization
{
    /// <summary>The super admins: the owners, identified at sign-in from the configured OwnerPhoneNumbers.</summary>
    public static bool IsSuperAdmin(ICurrentUser currentUser) =>
        currentUser.IsInRole(PlayerRole.Owner.ToString());
}

/// <summary>
/// Enforces the group rule on group-scoped actions and projects a session's group access for
/// read models. App-only sessions without a group are open; unresolved imported groups are closed.
/// </summary>
public interface IGroupMembershipGate
{
    /// <summary>
    /// Throws <see cref="GroupMembershipRequiredException"/> unless an app-only session has no group or
    /// the player holds an approved membership in it.
    /// </summary>
    Task EnsureCanJoinAsync(Session session, Guid playerProfileId, CancellationToken cancellationToken = default);

    /// <summary>Resolves the group and the player's standing for each session, in one pass.</summary>
    Task<IReadOnlyDictionary<Guid, SessionGroupAccess>> ResolveAccessAsync(
        IReadOnlyList<Session> sessions,
        Guid playerProfileId,
        CancellationToken cancellationToken = default);
}

public sealed class GroupMembershipGate(
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IGroupChatRepository groupChatRepository) : IGroupMembershipGate
{
    public async Task EnsureCanJoinAsync(Session session, Guid playerProfileId, CancellationToken cancellationToken = default)
    {
        if (session.GroupChatId is not { } groupChatId)
        {
            if (HasUnresolvedImportedGroup(session))
            {
                throw new GroupMembershipRequiredException("this group");
            }

            return;
        }

        if (await playerGroupLinkRepository.ExistsApprovedAsync(playerProfileId, groupChatId, cancellationToken))
        {
            return;
        }

        var group = await groupChatRepository.GetByIdAsync(groupChatId, cancellationToken);
        throw new GroupMembershipRequiredException(group?.GroupName ?? "this group");
    }

    public async Task<IReadOnlyDictionary<Guid, SessionGroupAccess>> ResolveAccessAsync(
        IReadOnlyList<Session> sessions,
        Guid playerProfileId,
        CancellationToken cancellationToken = default)
    {
        var groupIds = sessions
            .Where(session => session.GroupChatId is not null)
            .Select(session => session.GroupChatId!.Value)
            .Distinct()
            .ToArray();
        if (groupIds.Length == 0)
        {
            return sessions.ToDictionary(session => session.Id, ResolveUngroupedAccess);
        }

        var groupsById = (await groupChatRepository.ListByIdsAsync(groupIds, cancellationToken))
            .ToDictionary(group => group.Id);
        var statusByGroupId = (await playerGroupLinkRepository.ListByPlayerAsync(playerProfileId, cancellationToken))
            .ToDictionary(link => link.GroupChatId, link => link.Status);

        return sessions.ToDictionary(
            session => session.Id,
            session =>
            {
                if (session.GroupChatId is not { } groupChatId)
                {
                    return ResolveUngroupedAccess(session);
                }

                var status = statusByGroupId.TryGetValue(groupChatId, out var found) ? found : (GroupMembershipStatus?)null;
                return new SessionGroupAccess(
                    groupChatId,
                    groupsById.GetValueOrDefault(groupChatId)?.GroupName,
                    status,
                    CanJoin: status == GroupMembershipStatus.Approved);
            });
    }

    internal static bool HasUnresolvedImportedGroup(Session session) =>
        session.GroupChatId is null
        && (session.PickupPalOrigin == PickupPalOrigin.Imported
            || (session.PickupPalOrigin != PickupPalOrigin.CreatedByApp
                && session.OccurrenceKey?.StartsWith("pickuppal:", StringComparison.Ordinal) == true));

    private static SessionGroupAccess ResolveUngroupedAccess(Session session) =>
        HasUnresolvedImportedGroup(session)
            ? new SessionGroupAccess(null, "this group", null, CanJoin: false)
            : SessionGroupAccess.Open;
}
