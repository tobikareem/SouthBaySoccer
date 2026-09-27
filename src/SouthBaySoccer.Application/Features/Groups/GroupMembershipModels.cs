using System;
using System.Collections.Generic;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Every group with the caller's own membership state (the join screen).</summary>
public sealed record GetGroupCatalogQuery;

/// <summary>The caller's membership rows in every status plus their global standing.</summary>
public sealed record GetMyGroupMembershipsQuery;

/// <summary>
/// Multi-select join. Each group becomes Pending, or Approved when Pickup Pal already lists the
/// caller in that WhatsApp group. Existing pending / approved rows are left alone (idempotent).
/// </summary>
public sealed record RequestGroupMembershipsCommand(IReadOnlyList<Guid> GroupChatIds);

/// <summary>The caller leaves a group (their row becomes Removed, by themselves).</summary>
public sealed record LeaveGroupCommand(Guid GroupChatId);

/// <summary>A group's pending requests and members, for a group admin or super admin.</summary>
public sealed record GetGroupMembersQuery(Guid GroupChatId);

/// <summary>What a group admin does with a member row.</summary>
public enum GroupMemberReview
{
    Approve,
    Decline,
    Remove,
}

/// <summary>A group admin (or super admin) approves, declines, or removes a member of their group.</summary>
public sealed record ReviewGroupMemberCommand(Guid GroupChatId, Guid PlayerProfileId, GroupMemberReview Review);

/// <summary>A super admin adds a known player straight into a group as an approved member.</summary>
public sealed record AddGroupMemberCommand(Guid GroupChatId, Guid PlayerProfileId);

/// <summary>A super admin appoints or revokes a group admin; the player must be an approved member.</summary>
public sealed record SetGroupAdminCommand(Guid GroupChatId, Guid PlayerProfileId, bool IsAdmin);

/// <summary>Name-fragment search over player profiles (super admins and group admins only).</summary>
public sealed record SearchPlayersQuery(string Query);

public sealed record GroupWithMembershipModel(
    Guid GroupChatId,
    string GroupName,
    int MemberCount,
    GroupMembershipStatus? Status,
    GroupMemberRole Role,
    int PendingRequestCount);

public sealed record GroupCatalogModel(IReadOnlyList<GroupWithMembershipModel> Groups);

public sealed record GroupMembershipModel(
    Guid GroupChatId,
    string GroupName,
    GroupMembershipStatus Status,
    GroupMemberRole Role,
    GroupMembershipSource Source,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc);

public sealed record MyGroupMembershipsModel(
    bool IsSuperAdmin,
    bool HasApprovedGroup,
    IReadOnlyList<GroupMembershipModel> Memberships);

public sealed record GroupMemberModel(
    Guid PlayerProfileId,
    string DisplayName,
    string Initials,
    GroupMembershipStatus Status,
    GroupMemberRole Role,
    GroupMembershipSource Source,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc);

public sealed record GroupMembersModel(
    Guid GroupChatId,
    string GroupName,
    bool CanManageMembers,
    bool CanAppointAdmins,
    IReadOnlyList<GroupMemberModel> Pending,
    IReadOnlyList<GroupMemberModel> Members);

public sealed record PlayerSearchResultModel(
    Guid PlayerProfileId,
    string DisplayName,
    string Initials,
    string? MaskedPhone);

/// <summary>
/// A session's group and the caller's standing in it, for the feed / detail / Game Day
/// projections. <see cref="CanJoin"/> is true when the session has no group or the caller is an
/// approved member of it.
/// </summary>
public sealed record SessionGroupAccess(
    Guid? GroupChatId,
    string? GroupName,
    GroupMembershipStatus? MembershipStatus,
    bool CanJoin)
{
    /// <summary>A session without a group is open to every signed-in player.</summary>
    public static readonly SessionGroupAccess Open = new(null, null, null, CanJoin: true);
}
