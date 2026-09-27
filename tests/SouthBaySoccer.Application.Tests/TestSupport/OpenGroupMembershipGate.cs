using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Domain.Entities.Scheduling;

namespace SouthBaySoccer.Application.Tests.TestSupport;

/// <summary>
/// A gate that treats every session as open (no group, or the caller is an approved member), for
/// tests that exercise behaviour other than the GRP-1 membership rule.
/// </summary>
internal sealed class OpenGroupMembershipGate : IGroupMembershipGate
{
    public Task EnsureCanJoinAsync(Session session, Guid playerProfileId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<Guid, SessionGroupAccess>> ResolveAccessAsync(
        IReadOnlyList<Session> sessions,
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, SessionGroupAccess>>(
            sessions.ToDictionary(session => session.Id, _ => SessionGroupAccess.Open));
}
