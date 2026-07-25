using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

/// <summary>
/// Seed implementation of <see cref="IGroupsClient"/> for local demos. The demo player is already
/// linked (primary: "Bay Area Soccer") so the sign-in link step never blocks a seed run, while the
/// leaderboard group filter still has groups to switch between.
/// </summary>
public sealed class SeedGroupsClient : IGroupsClient
{
    private static readonly GroupChatDto BayArea = new(
        Guid.Parse("50000000-0000-0000-0000-000000000001"),
        "15166436091-1605317459@g.us",
        "Bay Area Soccer",
        349,
        IsLinked: true,
        IsPrimary: true);

    private static readonly GroupChatDto Morning = new(
        Guid.Parse("50000000-0000-0000-0000-000000000002"),
        "120363365205658538@g.us",
        "Morning Pick Up Soccer",
        67,
        IsLinked: true,
        IsPrimary: false);

    private static readonly GroupChatDto Saturday = new(
        Guid.Empty,
        "14088237661-1451531951@g.us",
        "Saturday Soccer",
        58,
        IsLinked: false,
        IsPrimary: false);

    public Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GroupChatDto>>([BayArea, Morning, Saturday]);
    }

    public Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MyGroupsResponse(IsLinked: true, [BayArea, Morning]));
    }

    public Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new MyGroupsResponse(IsLinked: true, [BayArea, Morning]));
    }
}
