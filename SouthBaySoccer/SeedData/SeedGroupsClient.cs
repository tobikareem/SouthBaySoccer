using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

/// <summary>
/// Seed implementation of <see cref="IGroupsClient"/> for local demos. The demo player is already
/// linked (primary: "Bay Area Soccer") so the sign-in link step never blocks a seed run, while the
/// leaderboard group filter still has groups to switch between.
/// </summary>
/// <remarks>
/// Membership fixtures (GRP-1): three groups. The seed player is an Approved admin of Bay Area
/// Soccer, Pending in Morning Pick Up Soccer, and not in Saturday Soccer, and is a super admin so
/// every membership screen can be demoed. Bay Area has two pending requests. Writes mutate this
/// singleton's in-memory state for the rest of the demo run; nothing is persisted.
/// </remarks>
public sealed class SeedGroupsClient : IGroupsClient
{
    public static readonly Guid BayAreaId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    public static readonly Guid MorningId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    public static readonly Guid SaturdayId = Guid.Parse("50000000-0000-0000-0000-000000000003");

    private static readonly DateTime RequestedAtUtc = new(2026, 6, 3, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ApprovedAtUtc = new(2026, 6, 3, 16, 5, 0, DateTimeKind.Utc);

    private static readonly GroupChatDto BayArea = new(
        BayAreaId,
        "15166436091-1605317459@g.us",
        "Bay Area Soccer",
        349,
        IsLinked: true,
        IsPrimary: true);

    private static readonly GroupChatDto Morning = new(
        MorningId,
        "120363365205658538@g.us",
        "Morning Pick Up Soccer",
        67,
        IsLinked: true,
        IsPrimary: false);

    private static readonly GroupChatDto Saturday = new(
        SaturdayId,
        "14088237661-1451531951@g.us",
        "Saturday Soccer",
        58,
        IsLinked: false,
        IsPrimary: false);

    private readonly object gate = new();
    private readonly Dictionary<Guid, SeedGroup> groups;

    public SeedGroupsClient()
    {
        groups = new Dictionary<Guid, SeedGroup>
        {
            [BayAreaId] = new(
                BayArea,
                [
                    Member(SeedFixtures.Players[0], GroupMemberRoles.Admin, GroupMembershipSources.WhatsApp),
                    Member(SeedFixtures.Players[1], GroupMemberRoles.Member, GroupMembershipSources.WhatsApp),
                    Member(SeedFixtures.Players[2], GroupMemberRoles.Member, GroupMembershipSources.Request),
                    Member(SeedFixtures.Players[3], GroupMemberRoles.Member, GroupMembershipSources.WhatsApp),
                ],
                [
                    Pending(SeedFixtures.Players[5]),
                    Pending(SeedFixtures.Players[6]),
                ]),
            [MorningId] = new(
                Morning,
                [
                    Member(SeedFixtures.Players[1], GroupMemberRoles.Admin, GroupMembershipSources.WhatsApp),
                    Member(SeedFixtures.Players[7], GroupMemberRoles.Member, GroupMembershipSources.WhatsApp),
                ],
                [
                    Pending(SeedFixtures.Players[0]),
                ]),
            [SaturdayId] = new(
                Saturday,
                [
                    Member(SeedFixtures.Players[3], GroupMemberRoles.Admin, GroupMembershipSources.WhatsApp),
                ],
                []),
        };
    }

    public Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GroupChatDto>>([BayArea, Morning, Saturday]);
    }

    // Legacy gate: linked = at least one Approved membership in the current (mutable) demo state, so
    // leaving a group on "My groups" is reflected on Sessions Home, the leaderboard filter, and the
    // sign-in gate. Pending Morning stays listed as it did before GRP-1 for the leaderboard filter.
    public Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult(BuildMyGroups());
        }
    }

    public Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult(BuildMyGroups());
        }
    }

    public Task<IReadOnlyList<GroupWithMembershipDto>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<GroupWithMembershipDto>>(
                groups.Values.Select(group => group.ToCatalogEntry(SeedFixtures.CurrentPlayerId)).ToArray());
        }
    }

    public Task<MyGroupMembershipsResponse> GetMyMembershipsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult(BuildMyMemberships());
        }
    }

    public Task<MyGroupMembershipsResponse> RequestMembershipsAsync(
        IReadOnlyList<Guid> groupChatIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            foreach (var groupChatId in groupChatIds)
            {
                if (!groups.TryGetValue(groupChatId, out var group) || group.Contains(SeedFixtures.CurrentPlayerId))
                {
                    continue;
                }

                // Seed rule standing in for the server's Pickup Pal check: Bay Area already lists the
                // demo player on WhatsApp, so it approves instantly; every other group waits for an admin.
                if (groupChatId == BayAreaId)
                {
                    group.Members.Add(Member(SeedFixtures.Players[0], GroupMemberRoles.Member, GroupMembershipSources.WhatsApp));
                }
                else
                {
                    group.Pending.Add(Pending(SeedFixtures.Players[0]));
                }
            }

            return Task.FromResult(BuildMyMemberships());
        }
    }

    public Task LeaveAsync(Guid groupChatId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (groups.TryGetValue(groupChatId, out var group))
            {
                group.Members.RemoveAll(member => member.PlayerProfileId == SeedFixtures.CurrentPlayerId);
                group.Pending.RemoveAll(member => member.PlayerProfileId == SeedFixtures.CurrentPlayerId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<GroupMembersResponse> GetMembersAsync(Guid groupChatId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var group = Require(groupChatId);
            var isAdmin = group.Members.Any(member =>
                member.PlayerProfileId == SeedFixtures.CurrentPlayerId && member.Role == GroupMemberRoles.Admin);
            return Task.FromResult(new GroupMembersResponse(
                group.Chat.Id,
                group.Chat.GroupName,
                CanManageMembers: isAdmin || IsSuperAdmin,
                CanAppointAdmins: IsSuperAdmin,
                group.Pending.ToArray(),
                group.Members.ToArray()));
        }
    }

    public Task ApproveAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var group = Require(groupChatId);
            var request = group.Pending.FirstOrDefault(member => member.PlayerProfileId == playerProfileId);
            if (request is not null)
            {
                group.Pending.Remove(request);
                group.Members.Add(request with
                {
                    Status = GroupMembershipStatuses.Approved,
                    ApprovedAtUtc = ApprovedAtUtc,
                });
            }
        }

        return Task.CompletedTask;
    }

    public Task DeclineAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Require(groupChatId).Pending.RemoveAll(member => member.PlayerProfileId == playerProfileId);
        }

        return Task.CompletedTask;
    }

    public Task RemoveMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            Require(groupChatId).Members.RemoveAll(member => member.PlayerProfileId == playerProfileId);
        }

        return Task.CompletedTask;
    }

    public Task AddMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var group = Require(groupChatId);
            var player = SeedFixtures.Players.FirstOrDefault(candidate => candidate.Id == playerProfileId)
                ?? throw new InvalidOperationException("The player is not in the seed directory.");
            if (!group.Contains(playerProfileId))
            {
                group.Pending.RemoveAll(member => member.PlayerProfileId == playerProfileId);
                group.Members.Add(Member(player, GroupMemberRoles.Member, GroupMembershipSources.SuperAdmin));
            }
        }

        return Task.CompletedTask;
    }

    public Task SetAdminAsync(Guid groupChatId, Guid playerProfileId, bool isAdmin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var group = Require(groupChatId);
            var index = group.Members.FindIndex(member => member.PlayerProfileId == playerProfileId);
            if (index < 0)
            {
                throw new InvalidOperationException("Only an approved member can be made a group admin.");
            }

            group.Members[index] = group.Members[index] with
            {
                Role = isAdmin ? GroupMemberRoles.Admin : GroupMemberRoles.Member,
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlayerSearchResultDto>> SearchPlayersAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fragment = query.Trim();
        if (fragment.Length < 2)
        {
            return Task.FromResult<IReadOnlyList<PlayerSearchResultDto>>([]);
        }

        return Task.FromResult<IReadOnlyList<PlayerSearchResultDto>>(SeedFixtures.Players
            .Where(player => player.DisplayName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(player => new PlayerSearchResultDto(player.Id, player.DisplayName, player.Initials, MaskedPhone: null))
            .ToArray());
    }

    // The seed player is one of the two owners so the super-admin screens are reachable in a demo.
    private const bool IsSuperAdmin = true;

    private MyGroupsResponse BuildMyGroups()
    {
        var mine = groups.Values
            .Where(group => group.Contains(SeedFixtures.CurrentPlayerId))
            .Select(group => group.Chat with
            {
                IsLinked = group.Members.Any(member => member.PlayerProfileId == SeedFixtures.CurrentPlayerId),
            })
            .ToArray();
        return new MyGroupsResponse(IsLinked: mine.Any(group => group.IsLinked), mine);
    }

    private MyGroupMembershipsResponse BuildMyMemberships()
    {
        var memberships = new List<GroupMembershipDto>();
        foreach (var group in groups.Values)
        {
            var member = group.Members.FirstOrDefault(m => m.PlayerProfileId == SeedFixtures.CurrentPlayerId)
                ?? group.Pending.FirstOrDefault(m => m.PlayerProfileId == SeedFixtures.CurrentPlayerId);
            if (member is not null)
            {
                memberships.Add(new GroupMembershipDto(
                    group.Chat.Id,
                    group.Chat.GroupName,
                    member.Status,
                    member.Role,
                    member.Source,
                    member.RequestedAtUtc,
                    member.ApprovedAtUtc));
            }
        }

        return new MyGroupMembershipsResponse(
            IsSuperAdmin,
            HasApprovedGroup: memberships.Any(m => m.Status == GroupMembershipStatuses.Approved),
            memberships);
    }

    private SeedGroup Require(Guid groupChatId) =>
        groups.TryGetValue(groupChatId, out var group)
            ? group
            : throw new InvalidOperationException("The group does not exist in the seed catalogue.");

    private static GroupMemberDto Member(PlayerSummaryDto player, string role, string source) =>
        new(player.Id, player.DisplayName, player.Initials, GroupMembershipStatuses.Approved, role, source, RequestedAtUtc, ApprovedAtUtc);

    private static GroupMemberDto Pending(PlayerSummaryDto player) =>
        new(player.Id, player.DisplayName, player.Initials, GroupMembershipStatuses.Pending, GroupMemberRoles.Member, GroupMembershipSources.Request, RequestedAtUtc, null);

    private sealed record SeedGroup(GroupChatDto Chat, List<GroupMemberDto> Members, List<GroupMemberDto> Pending)
    {
        public bool Contains(Guid playerProfileId) =>
            Members.Any(member => member.PlayerProfileId == playerProfileId)
            || Pending.Any(member => member.PlayerProfileId == playerProfileId);

        public GroupWithMembershipDto ToCatalogEntry(Guid playerProfileId)
        {
            var own = Members.FirstOrDefault(member => member.PlayerProfileId == playerProfileId)
                ?? Pending.FirstOrDefault(member => member.PlayerProfileId == playerProfileId);
            return new GroupWithMembershipDto(
                Chat.Id,
                Chat.GroupName,
                Chat.MemberCount,
                own?.Status ?? GroupMembershipStatuses.None,
                own?.Role ?? GroupMemberRoles.Member,
                Pending.Count);
        }
    }
}
