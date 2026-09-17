using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Groups;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for <see cref="GroupChat"/> aggregates mirrored from the external Pickup Pal API.
/// </summary>
public interface IGroupChatRepository : IRepository<GroupChat>
{
    /// <summary>
    /// Finds a group chat by its stable external id (the WhatsApp <c>…@g.us</c> id).
    /// </summary>
    Task<GroupChat?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the group chats with the given ids in one query (for labelling a page of sessions).
    /// </summary>
    Task<IReadOnlyList<GroupChat>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the group chats with the given external ids in one query (the import matches a
    /// game's group to the persisted catalogue this way; nothing is created).
    /// </summary>
    Task<IReadOnlyList<GroupChat>> ListByExternalIdsAsync(
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every group chat, by name. The catalogue is a handful of WhatsApp groups, so this is
    /// bounded by design (it is not a growing table).
    /// </summary>
    Task<IReadOnlyList<GroupChat>> ListAllAsync(CancellationToken cancellationToken = default);
}
