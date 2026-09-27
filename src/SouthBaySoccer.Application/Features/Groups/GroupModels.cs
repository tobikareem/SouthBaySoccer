using System;
using System.Collections.Generic;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Lists every available group chat for the sign-in selection list.</summary>
public sealed record GetAvailableGroupsQuery;

/// <summary>Returns the current player's linked groups and whether they are linked at all.</summary>
public sealed record GetMyGroupsQuery;

/// <summary>Links the current player to a group chat in our database (no external write).</summary>
public sealed record LinkPlayerToGroupCommand(string GroupExternalId);

/// <summary>A group chat projected for the client, with the current player's link state.</summary>
public sealed record GroupSummary(
    Guid Id,
    string ExternalId,
    string GroupName,
    int MemberCount,
    bool IsLinked,
    bool IsPrimary);

/// <summary>
/// The current player's group membership. <see cref="IsLinked"/> is <see langword="true"/> when the
/// player belongs to at least one group (or cannot be linked at all, e.g. a guest), so the sign-in
/// gate does not block a player who has nothing to link.
/// </summary>
public sealed record MyGroupsResult(bool IsLinked, IReadOnlyList<GroupSummary> Groups);
