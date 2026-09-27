using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for <see cref="PlayerGroupLink"/> membership records tying player profiles to group
/// chats. Unless a member says otherwise, "member" and "linked" mean an
/// <see cref="GroupMembershipStatus.Approved"/> row: pending, declined, removed, and withdrawn
/// rows never count as membership for group-scoped reads.
/// </summary>
public interface IPlayerGroupLinkRepository : IRepository<PlayerGroupLink>
{
    /// <summary>
    /// Lists every membership row for a player, whatever its status (pending, approved, declined,
    /// removed), so a writer can decide whether to create a row or move an existing one.
    /// </summary>
    Task<IReadOnlyList<PlayerGroupLink>> ListByPlayerAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the player's approved memberships.
    /// </summary>
    Task<IReadOnlyList<PlayerGroupLink>> ListApprovedByPlayerAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the player holds an approved membership in the group chat.
    /// </summary>
    Task<bool> ExistsApprovedAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the supplied players with an approved membership in the specified group.</summary>
    Task<IReadOnlyList<Guid>> ListApprovedPlayerIdsAsync(
        Guid groupChatId,
        IReadOnlyCollection<Guid> playerProfileIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the player's approved groups joined to their group-chat details, primary first.
    /// </summary>
    Task<IReadOnlyList<PlayerGroupReadModel>> ListPlayerGroupsAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every membership row of a player joined to the group display fields, whatever its
    /// status, for the player's own memberships view.
    /// </summary>
    Task<IReadOnlyList<PlayerMembershipReadModel>> ListPlayerMembershipsAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the player's approved membership in a group, or <see langword="null"/> when none
    /// exists. Read without tracking. The link's creation time is the floor for anything scoped
    /// to "since this player joined".
    /// </summary>
    Task<PlayerGroupLink?> FindLinkAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the membership row between a player and a group in any status, tracked for update,
    /// or <see langword="null"/> when the pair has no row at all.
    /// </summary>
    Task<PlayerGroupLink?> FindMembershipAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every membership row of a group (any status) joined to the member's display fields,
    /// ordered by display name. Callers split pending requests from members.
    /// </summary>
    Task<IReadOnlyList<GroupMemberReadModel>> ListGroupMembersAsync(
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts approved members and pending requests for each of the given groups in one query.
    /// Groups without rows are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, GroupMembershipCounts>> CountByGroupAsync(
        IReadOnlyCollection<Guid> groupChatIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the players currently approved in a group, optionally excluding one profile.
    /// This is the app-side audience — the people who can actually receive an in-app broadcast —
    /// as opposed to the externally reported WhatsApp roster size.
    /// </summary>
    Task<int> CountMembersAsync(
        Guid groupChatId,
        Guid? excludingPlayerProfileId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Projection of a player's approved group link joined to the group-chat display fields.</summary>
public sealed record PlayerGroupReadModel(
    Guid GroupChatId,
    string ExternalId,
    string GroupName,
    int MemberCount,
    bool IsPrimary);

/// <summary>One membership row of a player, in any status, with the group's display name.</summary>
public sealed record PlayerMembershipReadModel(
    Guid GroupChatId,
    string GroupName,
    GroupMembershipStatus Status,
    GroupMemberRole Role,
    GroupMembershipSource Source,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc);

/// <summary>One membership row of a group, in any status, with the member's display name.</summary>
public sealed record GroupMemberReadModel(
    Guid PlayerProfileId,
    string DisplayName,
    GroupMembershipStatus Status,
    GroupMemberRole Role,
    GroupMembershipSource Source,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc);

/// <summary>Approved-member and pending-request counts of one group.</summary>
public sealed record GroupMembershipCounts(int ApprovedCount, int PendingCount);
