using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Groups;

public sealed class GroupHandlerTests
{
    private const string PickupPalUserId = "cmhv6brig00dm8i0g9t92otka";
    private const string ExternalId = "15166436091-1605317459@g.us";
    private static readonly DateTime NowUtc = new(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetMyGroups_WhenProfileHasNoPickupPalId_ReportsLinkedWithoutCallingApi()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = null };
        var groupClient = new Mock<IPickupPalGroupClient>();
        var links = new Mock<IPlayerGroupLinkRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            groupClient.Object,
            new Mock<IGroupChatRepository>().Object,
            links.Object,
            Service(groupClient, links, unitOfWork),
            unitOfWork.Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue();
        result.Groups.Should().BeEmpty();
        groupClient.Verify(x => x.GetLinkedGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyGroups_WhenPickupPalReportsMembership_SeedsApprovedWhatsAppMembershipAndMarksPrimary()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };

        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat(ExternalId, "Bay Area Soccer", "D98ACL", "SUBSCRIBED", 349, "America/Los_Angeles")]);

        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GroupChat?)null);
        GroupChat? addedGroup = null;
        groupChats.Setup(x => x.AddAsync(It.IsAny<GroupChat>(), It.IsAny<CancellationToken>()))
            .Callback<GroupChat, CancellationToken>((g, _) => addedGroup = g)
            .Returns(Task.CompletedTask);

        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        PlayerGroupLink? addedLink = null;
        links.Setup(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerGroupLink, CancellationToken>((l, _) => addedLink = l)
            .Returns(Task.CompletedTask);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(Guid.NewGuid(), ExternalId, "Bay Area Soccer", 349, true)]);

        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue();
        result.Groups.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
        addedGroup.Should().NotBeNull();
        addedLink.Should().NotBeNull();
        addedLink!.Status.Should().Be(GroupMembershipStatus.Approved, "WhatsApp membership is approved automatically");
        addedLink.Source.Should().Be(GroupMembershipSource.WhatsApp);
        addedLink.ApprovedAtUtc.Should().Be(NowUtc);
        addedLink.ApprovedByPlayerProfileId.Should().BeNull();
        addedLink.IsPrimary.Should().BeTrue("the first approved membership becomes the player's primary group");
        // Once for the new group row (so the membership can reference it), once for the membership.
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetMyGroups_WhenRowExistsInAnyStatus_NeverReseedsIt()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var stored = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer", LinkageCode = "D98ACL", WhatsAppMemberCount = 349, Status = "SUBSCRIBED", Timezone = "America/Los_Angeles" };
        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat(ExternalId, "Bay Area Soccer", "D98ACL", "SUBSCRIBED", 349, "America/Los_Angeles")]);
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var removed = new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, Status = GroupMembershipStatus.Removed };
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([removed]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeFalse("a removed membership is not an approved one");
        removed.Status.Should().Be(GroupMembershipStatus.Removed, "an admin removal is never undone by the sign-in read");
        links.Verify(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyGroups_WhenRequestIsPendingAndWhatsAppListsThePlayer_ApprovesIt()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var stored = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer", LinkageCode = "D98ACL", WhatsAppMemberCount = 349, Status = "SUBSCRIBED", Timezone = "America/Los_Angeles" };
        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat(ExternalId, "Bay Area Soccer", "D98ACL", "SUBSCRIBED", 349, "America/Los_Angeles")]);
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var pending = new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, Status = GroupMembershipStatus.Pending, Source = GroupMembershipSource.Request };
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([pending]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue();
        pending.Status.Should().Be(GroupMembershipStatus.Approved);
        pending.Source.Should().Be(GroupMembershipSource.WhatsApp);
        pending.IsPrimary.Should().BeTrue();
        links.Verify(x => x.Update(pending), Times.Once);
        links.Verify(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyGroups_WhenGroupMetadataChangedButAlreadyLinked_PersistsRefresh()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var stored = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Old Name", WhatsAppMemberCount = 100, Status = "SUBSCRIBED" };

        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat(ExternalId, "New Name", "D98ACL", "SUBSCRIBED", 349, null)]);

        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, IsPrimary = true, Status = GroupMembershipStatus.Approved }]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "New Name", 349, true)]);

        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        await handler.HandleAsync(new GetMyGroupsQuery());

        stored.GroupName.Should().Be("New Name");
        stored.WhatsAppMemberCount.Should().Be(349);
        groupChats.Verify(x => x.Update(stored), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once,
            "refreshed group metadata must persist even when no new membership is added");
    }

    [Fact]
    public async Task GetMyGroups_WhenNothingChanged_DoesNotWrite()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var stored = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer", LinkageCode = "D98ACL", WhatsAppMemberCount = 349, Status = "SUBSCRIBED", Timezone = "America/Los_Angeles" };

        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat(ExternalId, "Bay Area Soccer", "D98ACL", "SUBSCRIBED", 349, "America/Los_Angeles")]);
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, IsPrimary = true, Status = GroupMembershipStatus.Approved }]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        await handler.HandleAsync(new GetMyGroupsQuery());

        groupChats.Verify(x => x.Update(It.IsAny<GroupChat>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyGroups_WhenPickupPalReadThrows_FallsBackToDatabaseLinks()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(Guid.NewGuid(), ExternalId, "Bay Area Soccer", 349, true)]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            new Mock<IGroupChatRepository>().Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue("our database drives linkage even when the external read fails");
        result.Groups.Should().ContainSingle();
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenAlreadyApproved_ReturnsStateWithoutAddingOrSaving()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var group = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer" };
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, Status = GroupMembershipStatus.Approved, IsPrimary = true }]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var groupClient = new Mock<IPickupPalGroupClient>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new LinkPlayerToGroupCommand(ExternalId));

        result.IsLinked.Should().BeTrue();
        links.Verify(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenConcurrentLinkConflicts_SwallowsAndReturnsCurrentState()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var group = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer" };
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var groupClient = new Mock<IPickupPalGroupClient>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationConflictException("duplicate link"));

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new LinkPlayerToGroupCommand(ExternalId));

        result.IsLinked.Should().BeTrue("linking is idempotent: a concurrent-link conflict returns current state, not an error");
        result.Groups.Should().ContainSingle();
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenNotOnWhatsApp_CreatesPendingRequestWithoutExternalWrite()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var existingGroup = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer" };

        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(existingGroup);

        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        PlayerGroupLink? addedLink = null;
        links.Setup(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerGroupLink, CancellationToken>((l, _) => addedLink = l)
            .Returns(Task.CompletedTask);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new LinkPlayerToGroupCommand(ExternalId));

        result.IsLinked.Should().BeFalse("a pending request is not an approved membership");
        addedLink.Should().NotBeNull();
        addedLink!.GroupChatId.Should().Be(groupId);
        addedLink.Status.Should().Be(GroupMembershipStatus.Pending);
        addedLink.Source.Should().Be(GroupMembershipSource.Request);
        addedLink.RequestedAtUtc.Should().Be(NowUtc);
        addedLink.IsPrimary.Should().BeFalse();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenPickupPalListsPlayerInGroup_ApprovesAtOnce()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var existingGroup = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer" };
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(existingGroup);
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        PlayerGroupLink? addedLink = null;
        links.Setup(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerGroupLink, CancellationToken>((l, _) => addedLink = l)
            .Returns(Task.CompletedTask);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat(ExternalId, "Bay Area Soccer", null, "SUBSCRIBED", 349, null)]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var result = await handler.HandleAsync(new LinkPlayerToGroupCommand(ExternalId));

        result.IsLinked.Should().BeTrue();
        addedLink!.Status.Should().Be(GroupMembershipStatus.Approved);
        addedLink.Source.Should().Be(GroupMembershipSource.WhatsApp);
        addedLink.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenGroupUnknown_ThrowsNotFound()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GroupChat?)null);
        var groupClient = new Mock<IPickupPalGroupClient>();
        groupClient.Setup(x => x.GetAllGroupsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var links = new Mock<IPlayerGroupLinkRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, links.Object, Service(groupClient, links, unitOfWork), unitOfWork.Object);

        var act = async () => await handler.HandleAsync(new LinkPlayerToGroupCommand("does-not-exist@g.us"));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }

    private static GroupMembershipService Service(
        Mock<IPickupPalGroupClient> groupClient,
        Mock<IPlayerGroupLinkRepository> links,
        Mock<IUnitOfWork> unitOfWork)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
        return new GroupMembershipService(clock.Object, groupClient.Object, links.Object, unitOfWork.Object);
    }

    private static Mock<ICurrentUser> CurrentUser(Guid identityUserId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        return currentUser;
    }

    private static Mock<IPlayerProfileRepository> Profiles(Guid identityUserId, PlayerProfile profile)
    {
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        return profiles;
    }
}
