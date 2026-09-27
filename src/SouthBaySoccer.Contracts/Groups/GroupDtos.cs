using System;
using System.Collections.Generic;

namespace SouthBaySoccer.Contracts.Groups;

/// <summary>A WhatsApp group chat projected for the client, with the current player's link state.</summary>
public sealed record GroupChatDto(
    Guid Id,
    string ExternalId,
    string GroupName,
    int MemberCount,
    bool IsLinked,
    bool IsPrimary);

/// <summary>The current player's group membership and whether the sign-in link step is satisfied.</summary>
public sealed record MyGroupsResponse(bool IsLinked, IReadOnlyList<GroupChatDto> Groups);

/// <summary>The full list of group chats a player can link to at sign-in.</summary>
public sealed record AvailableGroupsResponse(IReadOnlyList<GroupChatDto> Groups);

/// <summary>Request to link the current player to a group chat, identified by its external id.</summary>
public sealed record LinkGroupRequest(string GroupExternalId);
