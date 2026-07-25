using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Groups;

public sealed class GroupHandlerTests
{
    private const string PickupPalUserId = "cmhv6brig00dm8i0g9t92otka";
    private const string ExternalId = "15166436091-1605317459@g.us";

    [Fact]
    public async Task GetMyGroups_WhenProfileHasNoPickupPalId_ReportsLinkedWithoutCallingApi()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = null };
        var currentUser = CurrentUser(identityUserId);
        var profiles = Profiles(identityUserId, profile);
        var groupClient = new Mock<IPickupPalGroupClient>();

        var handler = new GetMyGroupsQueryHandler(
            currentUser.Object,
            profiles.Object,
            groupClient.Object,
            new Mock<IGroupChatRepository>().Object,
            new Mock<IPlayerGroupLinkRepository>().Object,
            new Mock<IUnitOfWork>().Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue();
        result.Groups.Should().BeEmpty();
        groupClient.Verify(x => x.GetLinkedGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyGroups_WhenPickupPalReportsMembership_SeedsDatabaseAndMarksPrimary()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var currentUser = CurrentUser(identityUserId);
        var profiles = Profiles(identityUserId, profile);

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
            currentUser.Object, profiles.Object, groupClient.Object,
            groupChats.Object, links.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue();
        result.Groups.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
        addedGroup.Should().NotBeNull();
        addedLink!.IsPrimary.Should().BeTrue("the first link becomes the player's primary group");
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
        // Player is already linked to this group, so no new PlayerGroupLink is created.
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, IsPrimary = true }]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "New Name", 349, true)]);

        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, unitOfWork.Object);

        await handler.HandleAsync(new GetMyGroupsQuery());

        stored.GroupName.Should().Be("New Name");
        stored.WhatsAppMemberCount.Should().Be(349);
        groupChats.Verify(x => x.Update(stored), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once,
            "refreshed group metadata must persist even when no new link is added");
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
            .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupId, IsPrimary = true }]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            groupChats.Object, links.Object, unitOfWork.Object);

        await handler.HandleAsync(new GetMyGroupsQuery());

        groupChats.Verify(x => x.Update(It.IsAny<GroupChat>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenAlreadyLinked_ReturnsStateWithoutAddingOrSaving()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var group = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer" };
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ExistsAsync(profile.Id, groupId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            new Mock<IPickupPalGroupClient>().Object, groupChats.Object, links.Object, unitOfWork.Object);

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
        links.Setup(x => x.ExistsAsync(profile.Id, groupId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationConflictException("duplicate link"));

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            new Mock<IPickupPalGroupClient>().Object, groupChats.Object, links.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(new LinkPlayerToGroupCommand(ExternalId));

        result.IsLinked.Should().BeTrue("linking is idempotent: a concurrent-link conflict returns current state, not an error");
        result.Groups.Should().ContainSingle();
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

        var handler = new GetMyGroupsQueryHandler(
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object, groupClient.Object,
            new Mock<IGroupChatRepository>().Object, links.Object, new Mock<IUnitOfWork>().Object);

        var result = await handler.HandleAsync(new GetMyGroupsQuery());

        result.IsLinked.Should().BeTrue("our database drives linkage even when the external read fails");
        result.Groups.Should().ContainSingle();
    }

    [Fact]
    public async Task LinkPlayerToGroup_WhenGroupExists_PersistsLinkWithoutCallingExternalWrite()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, PickupPalUserId = PickupPalUserId };
        var groupId = Guid.NewGuid();
        var existingGroup = new GroupChat { Id = groupId, ExternalId = ExternalId, GroupName = "Bay Area Soccer" };

        var groupChats = new Mock<IGroupChatRepository>();
        groupChats.Setup(x => x.FindByExternalIdAsync(ExternalId, It.IsAny<CancellationToken>())).ReturnsAsync(existingGroup);

        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ExistsAsync(profile.Id, groupId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        links.Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        PlayerGroupLink? addedLink = null;
        links.Setup(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerGroupLink, CancellationToken>((l, _) => addedLink = l)
            .Returns(Task.CompletedTask);
        links.Setup(x => x.ListPlayerGroupsAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupReadModel(groupId, ExternalId, "Bay Area Soccer", 349, true)]);

        var groupClient = new Mock<IPickupPalGroupClient>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, links.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(new LinkPlayerToGroupCommand(ExternalId));

        result.IsLinked.Should().BeTrue();
        addedLink.Should().NotBeNull();
        addedLink!.GroupChatId.Should().Be(groupId);
        addedLink.IsPrimary.Should().BeTrue();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // The external API is read-only: linking never issues a write to Pickup Pal.
        groupClient.Verify(x => x.GetLinkedGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        var handler = new LinkPlayerToGroupCommandHandler(
            new LinkPlayerToGroupCommandValidator(),
            CurrentUser(identityUserId).Object, Profiles(identityUserId, profile).Object,
            groupClient.Object, groupChats.Object, new Mock<IPlayerGroupLinkRepository>().Object, new Mock<IUnitOfWork>().Object);

        var act = async () => await handler.HandleAsync(new LinkPlayerToGroupCommand("does-not-exist@g.us"));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
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
