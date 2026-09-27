using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Client.Tests.TestSupport;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

/// <summary>GRP-1: API routes, cache invalidation, seed fixtures, and the Profile / super-admin entry points.</summary>
public class GroupMembershipClientsAndProfileTests
{
    private static readonly Guid GroupId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid PlayerId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly DateTime RequestedAtUtc = new(2026, 6, 3, 16, 0, 0, DateTimeKind.Utc);

    private const string MembershipsJson = """
        { "isSuperAdmin": true, "hasApprovedGroup": true, "memberships": [] }
        """;

    private const string CatalogJson = """
        { "groups": [ { "id": "50000000-0000-0000-0000-000000000001", "groupName": "Bay Area Soccer", "memberCount": 349,
                        "membershipStatus": "Approved", "memberRole": "Admin", "pendingRequestCount": 2 } ] }
        """;

    // ---- ApiGroupsClient routes ----------------------------------------------------------------

    [Theory]
    [InlineData("player@example.test")]
    [InlineData("+1 (555) 123-4567")]
    [InlineData("name 1234")]
    [InlineData("１２３４")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklm")]
    public async Task SearchPlayersAsync_InvalidNameFragment_DoesNotSendHttpRequest(string query)
    {
        var handler = new CountingHttpMessageHandler();
        var client = new ApiGroupsClient(CreateHttpClient(handler));

        var act = () => client.SearchPlayersAsync(query, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApiGroupsClient_ReadsAndWrites_HitTheContractRoutes()
    {
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/groups/catalog", CatalogJson);
        handler.RegisterJson("/players/me/memberships", MembershipsJson);
        handler.RegisterJson($"/groups/{GroupId:D}/members", """{ "groupChatId": "50000000-0000-0000-0000-000000000001", "groupName": "Bay Area Soccer", "canManageMembers": true, "canAppointAdmins": true, "pending": [], "members": [] }""");
        handler.RegisterJson("/players/search", """{ "players": [] }""");
        var client = new ApiGroupsClient(CreateHttpClient(handler));

        await client.GetCatalogAsync(CancellationToken.None);
        await client.GetMyMembershipsAsync(CancellationToken.None);
        await client.RequestMembershipsAsync([GroupId], CancellationToken.None);
        await client.LeaveAsync(GroupId, CancellationToken.None);
        await client.GetMembersAsync(GroupId, CancellationToken.None);
        await client.ApproveAsync(GroupId, PlayerId, CancellationToken.None);
        await client.DeclineAsync(GroupId, PlayerId, CancellationToken.None);
        await client.RemoveMemberAsync(GroupId, PlayerId, CancellationToken.None);
        await client.AddMemberAsync(GroupId, PlayerId, CancellationToken.None);
        await client.SetAdminAsync(GroupId, PlayerId, true, CancellationToken.None);
        await client.SearchPlayersAsync("de j", CancellationToken.None);

        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/groups/catalog"),
            (HttpMethod.Get, "/players/me/memberships"),
            (HttpMethod.Post, "/players/me/memberships/requests"),
            (HttpMethod.Delete, $"/players/me/memberships/{GroupId:D}"),
            (HttpMethod.Get, $"/groups/{GroupId:D}/members"),
            (HttpMethod.Post, $"/groups/{GroupId:D}/members/{PlayerId:D}/approve"),
            (HttpMethod.Post, $"/groups/{GroupId:D}/members/{PlayerId:D}/decline"),
            (HttpMethod.Post, $"/groups/{GroupId:D}/members/{PlayerId:D}/remove"),
            (HttpMethod.Post, $"/groups/{GroupId:D}/members"),
            (HttpMethod.Put, $"/groups/{GroupId:D}/admins"),
            (HttpMethod.Get, "/players/search?q=de%20j"));
    }

    [Fact]
    public async Task ApiGroupsClient_GetAvailableGroups_MapsTheCatalogueForLegacyCallers()
    {
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/groups/catalog", CatalogJson);
        var client = new ApiGroupsClient(CreateHttpClient(handler));

        var groups = await client.GetAvailableGroupsAsync(CancellationToken.None);

        groups.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = GroupId,
            GroupName = "Bay Area Soccer",
            MemberCount = 349,
            IsLinked = true,
        });
        handler.Requests.Should().Equal((HttpMethod.Get, "/groups/catalog"));
    }

    // ---- CachedGroupsClient ----------------------------------------------------------------------

    [Fact]
    public async Task CachedGroupsClient_ReadsAreCachedUnderGroupsPrefix_AndEveryWriteInvalidatesIt()
    {
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/groups/catalog", CatalogJson);
        handler.RegisterJson("/players/me/memberships", MembershipsJson);
        var cache = new ClientResponseCache(TimeProvider.System);
        IGroupsClient client = new CachedGroupsClient(new ApiGroupsClient(CreateHttpClient(handler)), cache);

        await client.GetCatalogAsync(CancellationToken.None);
        await client.GetCatalogAsync(CancellationToken.None);
        await client.GetMyMembershipsAsync(CancellationToken.None);
        await client.GetMyMembershipsAsync(CancellationToken.None);
        handler.Count("/groups/catalog").Should().Be(1);
        handler.Count("/players/me/memberships").Should().Be(1);

        await client.ApproveAsync(GroupId, PlayerId, CancellationToken.None);
        await client.GetCatalogAsync(CancellationToken.None);
        await client.GetMyMembershipsAsync(CancellationToken.None);

        handler.Count("/groups").Should().Be(3, "the catalogue read plus the approve post plus the re-read");
        handler.Count("/players/me/memberships").Should().Be(2);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("leave")]
    [InlineData("decline")]
    [InlineData("remove")]
    [InlineData("add")]
    [InlineData("setAdmin")]
    public async Task CachedGroupsClient_EachWrite_InvalidatesTheGroupsPrefix(string write)
    {
        var inner = new Mock<IGroupsClient>();
        inner.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, false, []));
        var cache = new Mock<IClientResponseCache>();
        IGroupsClient client = new CachedGroupsClient(inner.Object, cache.Object);

        await (write switch
        {
            "request" => client.RequestMembershipsAsync([GroupId], CancellationToken.None),
            "leave" => client.LeaveAsync(GroupId, CancellationToken.None),
            "decline" => client.DeclineAsync(GroupId, PlayerId, CancellationToken.None),
            "remove" => client.RemoveMemberAsync(GroupId, PlayerId, CancellationToken.None),
            "add" => client.AddMemberAsync(GroupId, PlayerId, CancellationToken.None),
            _ => client.SetAdminAsync(GroupId, PlayerId, false, CancellationToken.None),
        });

        cache.Verify(x => x.Invalidate("groups:"), Times.Once);
    }

    // ---- SeedGroupsClient ------------------------------------------------------------------------

    [Fact]
    public async Task SeedGroupsClient_Fixtures_MatchTheDemoStory()
    {
        var seed = new SeedGroupsClient();

        var memberships = await seed.GetMyMembershipsAsync(CancellationToken.None);
        var catalog = await seed.GetCatalogAsync(CancellationToken.None);
        var members = await seed.GetMembersAsync(SeedGroupsClient.BayAreaId, CancellationToken.None);

        memberships.IsSuperAdmin.Should().BeTrue();
        memberships.HasApprovedGroup.Should().BeTrue();
        catalog.Should().HaveCount(3);
        catalog.Single(g => g.Id == SeedGroupsClient.BayAreaId).MembershipStatus.Should().Be(GroupMembershipStatuses.Approved);
        catalog.Single(g => g.Id == SeedGroupsClient.MorningId).MembershipStatus.Should().Be(GroupMembershipStatuses.Pending);
        catalog.Single(g => g.Id == SeedGroupsClient.SaturdayId).MembershipStatus.Should().Be(GroupMembershipStatuses.None);
        members.Pending.Should().HaveCount(2);
        members.CanAppointAdmins.Should().BeTrue();
        (await seed.GetMyGroupsAsync(CancellationToken.None)).IsLinked.Should().BeTrue("the legacy gate must keep passing");
    }

    [Fact]
    public async Task SeedGroupsClient_Writes_MutateTheDemoState()
    {
        var seed = new SeedGroupsClient();
        var before = await seed.GetMembersAsync(SeedGroupsClient.BayAreaId, CancellationToken.None);
        var pendingPlayer = before.Pending[0].PlayerProfileId;

        await seed.ApproveAsync(SeedGroupsClient.BayAreaId, pendingPlayer, CancellationToken.None);
        await seed.SetAdminAsync(SeedGroupsClient.BayAreaId, pendingPlayer, true, CancellationToken.None);
        var requested = await seed.RequestMembershipsAsync([SeedGroupsClient.SaturdayId], CancellationToken.None);
        var after = await seed.GetMembersAsync(SeedGroupsClient.BayAreaId, CancellationToken.None);

        after.Pending.Should().ContainSingle();
        after.Members.Single(m => m.PlayerProfileId == pendingPlayer).Role.Should().Be(GroupMemberRoles.Admin);
        requested.Memberships.Single(m => m.GroupChatId == SeedGroupsClient.SaturdayId).Status.Should().Be(GroupMembershipStatuses.Pending);
        (await seed.SearchPlayersAsync("d", CancellationToken.None)).Should().BeEmpty("one character is below the minimum");
        (await seed.SearchPlayersAsync("de", CancellationToken.None)).Should().NotBeEmpty();

        await seed.LeaveAsync(SeedGroupsClient.BayAreaId, CancellationToken.None);

        (await seed.GetMyGroupsAsync(CancellationToken.None)).IsLinked.Should().BeFalse("the legacy gate follows the demo state");
    }

    [Fact]
    public async Task CachedGroupsClient_WriteThatThrows_StillInvalidatesTheGroupsPrefix()
    {
        var inner = new Mock<IGroupsClient>();
        inner.Setup(x => x.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));
        var cache = new Mock<IClientResponseCache>();
        IGroupsClient client = new CachedGroupsClient(inner.Object, cache.Object);

        var act = () => client.ApproveAsync(GroupId, PlayerId, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        cache.Verify(x => x.Invalidate("groups:"), Times.Once, "a timed-out write may still have been applied");
    }

    // ---- SuperAdminGroupsPageModel ---------------------------------------------------------------

    [Fact]
    public async Task SuperAdminGroups_ListsCatalogueWithCounts_AndOpensMembersForTheTappedGroup()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetCatalogAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new GroupWithMembershipDto(GroupId, "Bay Area Soccer", 349, GroupMembershipStatuses.Approved, GroupMemberRoles.Admin, 2),
        ]);
        var navigator = new Mock<IProfileNavigator>();
        var pageModel = new SuperAdminGroupsPageModel(groups.Object, navigator.Object, new ClientResponseCache(TimeProvider.System));

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenGroupCommand.ExecuteAsync(pageModel.Groups[0]);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Groups[0].Detail.Should().Be("349 members · 2 pending requests");
        pageModel.Groups[0].HasPending.Should().BeTrue();
        navigator.Verify(x => x.OpenGroupMembersAsync(GroupId), Times.Once);
    }

    // ---- ProfilePageModel entry points -----------------------------------------------------------

    [Fact]
    public async Task Profile_OwnProfile_ExposesGroupsSummaryAdminGroupsAndSuperAdminFlag()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new MyGroupMembershipsResponse(IsSuperAdmin: true, HasApprovedGroup: true,
            [
                new GroupMembershipDto(GroupId, "Bay Area Soccer", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin, GroupMembershipSources.WhatsApp, RequestedAtUtc, RequestedAtUtc),
                new GroupMembershipDto(Guid.NewGuid(), "Morning Pick Up Soccer", GroupMembershipStatuses.Pending, GroupMemberRoles.Member, GroupMembershipSources.Request, RequestedAtUtc, null),
            ]));
        var navigator = new Mock<IProfileNavigator>();
        var pageModel = CreateProfile(groups, navigator);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.IsSuperAdmin.Should().BeTrue();
        pageModel.MyGroupsSummary.Should().Be("Bay Area Soccer, Morning Pick Up Soccer");
        pageModel.MyGroupsDetail.Should().Be("1 approved · 1 pending");
        pageModel.MyGroups.Select(group => group.StatusVariant).Should().Equal(BadgeVariant.Success, BadgeVariant.Warning);
        pageModel.AdminGroups.Select(group => group.Name).Should().Equal("Bay Area Soccer");
        pageModel.HasAdminGroups.Should().BeTrue();

        await pageModel.OpenMyGroupsCommand.ExecuteAsync(null);
        await pageModel.OpenGroupMembersCommand.ExecuteAsync(pageModel.AdminGroups[0]);
        await pageModel.OpenSuperAdminGroupsCommand.ExecuteAsync(null);

        navigator.Verify(x => x.OpenMyGroupsAsync(), Times.Once);
        navigator.Verify(x => x.OpenGroupMembersAsync(GroupId), Times.Once);
        navigator.Verify(x => x.OpenSuperAdminGroupsAsync(), Times.Once);
    }

    [Fact]
    public async Task Profile_PlainMember_HidesSuperAdminCardAndAdminGroups()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new MyGroupMembershipsResponse(IsSuperAdmin: false, HasApprovedGroup: true,
                [new GroupMembershipDto(GroupId, "Bay Area Soccer", GroupMembershipStatuses.Approved, GroupMemberRoles.Member, GroupMembershipSources.WhatsApp, RequestedAtUtc, RequestedAtUtc)]));
        var pageModel = CreateProfile(groups, new Mock<IProfileNavigator>());

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.IsSuperAdmin.Should().BeFalse();
        pageModel.HasAdminGroups.Should().BeFalse();
        pageModel.MyGroupsDetail.Should().Be("1 approved");
    }

    [Fact]
    public async Task Profile_WhenMembershipsFail_StillRendersTheProfile()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));
        var pageModel = CreateProfile(groups, new Mock<IProfileNavigator>());

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsSuperAdmin.Should().BeFalse();
        pageModel.MyGroupsSummary.Should().Be("You're not in a group yet.");
    }

    [Fact]
    public async Task Profile_OtherPlayer_DoesNotLoadMemberships()
    {
        var groups = new Mock<IGroupsClient>(MockBehavior.Strict);
        var profileClient = new Mock<IProfileClient>();
        profileClient.Setup(x => x.GetProfileAsync(PlayerId, It.IsAny<CancellationToken>())).ReturnsAsync(SeedFixtures.Profile);
        var pageModel = new ProfilePageModel(
            profileClient.Object, groups.Object, new Mock<IProfileExternalLauncher>().Object, new Mock<IProfileNavigator>().Object,
            new Mock<IAuthenticationCoordinator>().Object, new Mock<IUserDialogService>().Object, new ClientResponseCache(TimeProvider.System));
        pageModel.ApplyQueryAttributes(new Dictionary<string, object> { [ProfilePageModel.PlayerIdQueryKey] = PlayerId.ToString() });

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        groups.Verify(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProfilePageModel CreateProfile(Mock<IGroupsClient> groups, Mock<IProfileNavigator> navigator)
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient.Setup(x => x.GetCurrentProfileAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SeedFixtures.Profile);
        return new ProfilePageModel(
            profileClient.Object,
            groups.Object,
            new Mock<IProfileExternalLauncher>().Object,
            navigator.Object,
            new Mock<IAuthenticationCoordinator>().Object,
            new Mock<IUserDialogService>().Object,
            new ClientResponseCache(TimeProvider.System));
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.example.test/") };
}
