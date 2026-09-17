using System;
using System.Collections.Generic;

namespace SouthBaySoccer.Contracts.Groups;

/// <summary>Membership status values used on the wire (string form of the Domain enum).</summary>
public static class GroupMembershipStatuses
{
    public const string None = "None";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Declined = "Declined";
    public const string Removed = "Removed";
}

/// <summary>Membership role values used on the wire.</summary>
public static class GroupMemberRoles
{
    public const string Member = "Member";
    public const string Admin = "Admin";
}

/// <summary>How a membership came to exist.</summary>
public static class GroupMembershipSources
{
    public const string WhatsApp = "WhatsApp";
    public const string Request = "Request";
    public const string SuperAdmin = "SuperAdmin";
}

/// <summary>A group as seen by the caller, with their own membership state.</summary>
public sealed record GroupWithMembershipDto(
    Guid Id,
    string GroupName,
    int MemberCount,
    string MembershipStatus,
    string MemberRole,
    int PendingRequestCount);

public sealed record GroupCatalogResponse(IReadOnlyList<GroupWithMembershipDto> Groups);

/// <summary>The caller's memberships plus their global standing.</summary>
public sealed record MyGroupMembershipsResponse(
    bool IsSuperAdmin,
    bool HasApprovedGroup,
    IReadOnlyList<GroupMembershipDto> Memberships);

public sealed record GroupMembershipDto(
    Guid GroupChatId,
    string GroupName,
    string Status,
    string Role,
    string Source,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc);

/// <summary>Multi-select join: every id becomes Pending, or Approved when Pickup Pal already lists the caller in that WhatsApp group.</summary>
public sealed record RequestGroupMembershipsRequest(IReadOnlyList<Guid> GroupChatIds);

public sealed record GroupMemberDto(
    Guid PlayerProfileId,
    string DisplayName,
    string Initials,
    string Status,
    string Role,
    string Source,
    DateTime RequestedAtUtc,
    DateTime? ApprovedAtUtc);

/// <summary>Members view for a group admin or super admin.</summary>
public sealed record GroupMembersResponse(
    Guid GroupChatId,
    string GroupName,
    bool CanManageMembers,
    bool CanAppointAdmins,
    IReadOnlyList<GroupMemberDto> Pending,
    IReadOnlyList<GroupMemberDto> Members);

/// <summary>Super admin: add a known player straight into a group as an approved member.</summary>
public sealed record AddGroupMemberRequest(Guid PlayerProfileId);

/// <summary>Super admin: make or unmake a group admin. The player must already be an approved member.</summary>
public sealed record SetGroupAdminRequest(Guid PlayerProfileId, bool IsAdmin);

public sealed record PlayerSearchResultDto(
    Guid PlayerProfileId,
    string DisplayName,
    string Initials,
    string? MaskedPhone);

public sealed record PlayerSearchResponse(IReadOnlyList<PlayerSearchResultDto> Players);
