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
}
