using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public class GroupMembersPageModelTests
{
    private static readonly Guid GroupId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid AyoId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid KolaId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid DejiId = Guid.Parse("10000000-0000-0000-0000-000000000008");
    private static readonly DateTime RequestedAtUtc = new(2026, 6, 3, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Appearing_LoadsPendingAndMembersForTheRoutedGroup()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        var pageModel = CreatePageModel(groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        groups.Verify(x => x.GetMembersAsync(GroupId, It.IsAny<CancellationToken>()), Times.Once);
        pageModel.State.Should().Be(ViewState.Content);
        pageModel.GroupName.Should().Be("Bay Area Soccer");
        pageModel.Pending.Select(member => member.Name).Should().Equal("Ayo N.");
        pageModel.Pending[0].Detail.Should().StartWith("Requested ");
        pageModel.CurrentMembers.Select(member => member.Name).Should().Equal("Kola T.");
        pageModel.CurrentMembers[0].Detail.Should().Be("Member · via WhatsApp");
        pageModel.CanManageMembers.Should().BeTrue();
        pageModel.CanAppointAdmins.Should().BeFalse("group admins do not get the super-admin extras");
        pageModel.PendingCountText.Should().Be("1 pending");
    }

    [Fact]
    public async Task Appearing_WithoutGroupId_ShowsErrorInsteadOfCallingTheClient()
    {
        var groups = new Mock<IGroupsClient>(MockBehavior.Strict);
        var pageModel = new GroupMembersPageModel(
            groups.Object, new Mock<IProfileNavigator>().Object, DialogsAnswering(true).Object, new ClientResponseCache(TimeProvider.System));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be(GroupMembersPageModel.MissingGroupTitle);
    }

    [Fact]
    public async Task Approve_CallsApproveWithGroupAndPlayerIds_InvalidatesAndReloads()
    {
        var cache = new Mock<IClientResponseCache>();
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        var pageModel = CreatePageModel(groups, cache: cache);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.ApproveCommand.ExecuteAsync(pageModel.Pending[0]);

        groups.Verify(x => x.ApproveAsync(GroupId, AyoId, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.Invalidate("groups:"), Times.Once);
        groups.Verify(x => x.GetMembersAsync(GroupId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Decline_CallsDeclineWithGroupAndPlayerIds()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.DeclineCommand.ExecuteAsync(pageModel.Pending[0]);

        groups.Verify(x => x.DeclineAsync(GroupId, AyoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_WhenConfirmed_CallsRemoveWithGroupAndPlayerIds()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        var pageModel = CreatePageModel(groups, DialogsAnswering(true));
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.RemoveCommand.ExecuteAsync(pageModel.CurrentMembers[0]);

        groups.Verify(x => x.RemoveMemberAsync(GroupId, KolaId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_WhenNotConfirmed_DoesNotCallTheClient()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        var pageModel = CreatePageModel(groups, DialogsAnswering(false));
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.RemoveCommand.ExecuteAsync(pageModel.CurrentMembers[0]);

        groups.Verify(x => x.RemoveMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ToggleAdmin_WhenCannotAppointAdmins_IsIgnored()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.ToggleAdminCommand.ExecuteAsync(pageModel.CurrentMembers[0]);
        await pageModel.AddMemberCommand.ExecuteAsync(new PlayerSearchItem(DejiId, "Deji O.", "DO", null));

        groups.Verify(x => x.SetAdminAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        groups.Verify(x => x.AddMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ToggleAdmin_AsSuperAdmin_FlipsTheMembersRole()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: true));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.CurrentMembers[0].AdminActionDescription.Should().Be("Make Kola T. a group admin");
        pageModel.ShowsAdminBadge.Should().BeFalse("super admins get the crown toggle instead of the pill");

        await pageModel.ToggleAdminCommand.ExecuteAsync(pageModel.CurrentMembers[0]);

        groups.Verify(x => x.SetAdminAsync(GroupId, KolaId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchQuery_UnderTwoCharacters_DoesNotSearch()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: true));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SearchQuery = "d";
        await pageModel.PendingSearch;

        groups.Verify(x => x.SearchPlayersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        pageModel.SearchResults.Should().BeEmpty();
    }

    [Theory]
    [InlineData("player@example.test")]
    [InlineData("+1 (555) 123-4567")]
    [InlineData("１２３４")]
    public async Task SearchQuery_PersonalIdentifier_ClearsResultsAndDoesNotSearch(string query)
    {
        var groups = GroupsReturning(Members(canAppointAdmins: true));
        groups.Setup(x => x.SearchPlayersAsync("de", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerSearchResultDto(DejiId, "Deji O.", "DO", null)]);
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SearchQuery = "de";
        await pageModel.PendingSearch;

        pageModel.SearchQuery = query;
        await pageModel.PendingSearch;

        groups.Verify(x => x.SearchPlayersAsync(query, It.IsAny<CancellationToken>()), Times.Never);
        pageModel.SearchResults.Should().BeEmpty();
        pageModel.ActionMessage.Should().Contain("Search by name only");
    }

    [Fact]
    public async Task SearchQuery_TwoCharacters_SearchesAndHidesPlayersAlreadyInTheGroup()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: true));
        groups.Setup(x => x.SearchPlayersAsync("de", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PlayerSearchResultDto(DejiId, "Deji O.", "DO", null),
                new PlayerSearchResultDto(KolaId, "Kola T.", "KT", null),
            ]);
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SearchQuery = " de ";
        await pageModel.PendingSearch;

        groups.Verify(x => x.SearchPlayersAsync("de", It.IsAny<CancellationToken>()), Times.Once);
        pageModel.SearchResults.Select(player => player.Name).Should().Equal("Deji O.");
    }

    [Fact]
    public async Task SearchQuery_ChangedWhileSearching_DiscardsTheSupersededResult()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: true));
        var first = new TaskCompletionSource<IReadOnlyList<PlayerSearchResultDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        groups.Setup(x => x.SearchPlayersAsync("de", It.IsAny<CancellationToken>())).Returns(first.Task);
        groups.Setup(x => x.SearchPlayersAsync("dej", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerSearchResultDto(DejiId, "Deji O.", "DO", null)]);
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SearchQuery = "de";
        var superseded = pageModel.PendingSearch;
        pageModel.SearchQuery = "dej";
        await pageModel.PendingSearch;
        first.SetResult([new PlayerSearchResultDto(Guid.NewGuid(), "Delayed D.", "DD", null)]);
        await superseded;

        pageModel.SearchResults.Select(player => player.Name).Should().Equal("Deji O.");
    }

    [Fact]
    public async Task AddMember_AsSuperAdmin_AddsAndClearsTheSearch()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: true));
        groups.Setup(x => x.SearchPlayersAsync("de", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerSearchResultDto(DejiId, "Deji O.", "DO", null)]);
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SearchQuery = "de";
        await pageModel.PendingSearch;
        pageModel.SearchResults.Should().ContainSingle();

        await pageModel.AddMemberCommand.ExecuteAsync(new PlayerSearchItem(DejiId, "Deji O.", "DO", null));

        groups.Verify(x => x.AddMemberAsync(GroupId, DejiId, It.IsAny<CancellationToken>()), Times.Once);
        pageModel.SearchQuery.Should().BeEmpty();
        pageModel.SearchResults.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_WhenClientThrows_ShowsRecoverableMessage()
    {
        var groups = GroupsReturning(Members(canAppointAdmins: false));
        groups.Setup(x => x.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.ApproveCommand.ExecuteAsync(pageModel.Pending[0]);

        pageModel.ActionMessage.Should().Be(GroupMembersPageModel.ActionFailedMessage);
        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Pending.Should().ContainSingle();
    }

    private static GroupMembersPageModel CreatePageModel(
        Mock<IGroupsClient> groups,
        Mock<IUserDialogService>? dialogs = null,
        Mock<IClientResponseCache>? cache = null)
    {
        var pageModel = new GroupMembersPageModel(
            groups.Object,
            new Mock<IProfileNavigator>().Object,
            (dialogs ?? DialogsAnswering(true)).Object,
            cache?.Object ?? new ClientResponseCache(TimeProvider.System));
        // Shell delivers the query value as a string, exactly as the navigator builds it.
        pageModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [GroupMembersPageModel.GroupIdQueryKey] = GroupId.ToString("D"),
        });
        return pageModel;
    }

    private static GroupMembersResponse Members(bool canAppointAdmins) =>
        new(
            GroupId,
            "Bay Area Soccer",
            CanManageMembers: true,
            CanAppointAdmins: canAppointAdmins,
            Pending: [new GroupMemberDto(AyoId, "Ayo N.", "AN", GroupMembershipStatuses.Pending, GroupMemberRoles.Member, GroupMembershipSources.Request, RequestedAtUtc, null)],
            Members: [new GroupMemberDto(KolaId, "Kola T.", "KT", GroupMembershipStatuses.Approved, GroupMemberRoles.Member, GroupMembershipSources.WhatsApp, RequestedAtUtc, RequestedAtUtc)]);

    private static Mock<IGroupsClient> GroupsReturning(GroupMembersResponse members)
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetMembersAsync(GroupId, It.IsAny<CancellationToken>())).ReturnsAsync(members);
        groups.Setup(x => x.SearchPlayersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return groups;
    }

    private static Mock<IUserDialogService> DialogsAnswering(bool confirmed)
    {
        var dialogs = new Mock<IUserDialogService>();
        dialogs.Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(confirmed);
        return dialogs;
    }
}
