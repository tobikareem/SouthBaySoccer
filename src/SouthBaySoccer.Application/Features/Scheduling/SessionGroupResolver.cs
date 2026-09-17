using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>
/// Resolves the WhatsApp group a session belongs to. An explicit group must exist; when the
/// command carries none, the acting admin's only linked group is used if they have exactly one
/// active <see cref="PlayerGroupLink"/>, otherwise the session stays app-only (null). Group
/// membership is read from our own database only (see the group-chat read-only rule).
/// </summary>
public sealed class SessionGroupResolver(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IGroupChatRepository groupChatRepository)
{
    /// <summary>Returns the group to associate, or null for an app-only session.</summary>
    /// <exception cref="ApplicationNotFoundException">The requested group does not exist.</exception>
    public async Task<GroupChat?> ResolveAsync(Guid? requestedGroupChatId, CancellationToken cancellationToken = default)
    {
        if (requestedGroupChatId is { } groupChatId && groupChatId != Guid.Empty)
        {
            return await groupChatRepository.GetByIdAsync(groupChatId, cancellationToken)
                ?? throw new ApplicationNotFoundException("Group chat was not found.");
        }

        if (currentUser.UserId is not { } identityUserId)
        {
            return null;
        }

        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var links = await playerGroupLinkRepository.ListByPlayerAsync(profile.Id, cancellationToken);
        if (links.Count != 1)
        {
            return null;
        }

        return await groupChatRepository.GetByIdAsync(links[0].GroupChatId, cancellationToken);
    }

    /// <summary>Loads a session's group for display, or null when it has none.</summary>
    public Task<GroupChat?> FindAsync(Guid? groupChatId, CancellationToken cancellationToken = default) =>
        groupChatId is { } id
            ? groupChatRepository.GetByIdAsync(id, cancellationToken)
            : Task.FromResult<GroupChat?>(null);
}
