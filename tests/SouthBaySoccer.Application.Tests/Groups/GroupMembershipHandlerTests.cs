using FluentAssertions;
using FluentValidation;
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

public sealed class GroupMembershipHandlerTests
{
    private const string PickupPalUserId = "cmhv6brig00dm8i0g9t92otka";
    private static readonly DateTime NowUtc = new(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc);

    // ----- Request flow -----

    [Fact]
    public async Task RequestMemberships_WhenPickupPalListsOneGroupOnly_ApprovesThatOneAndLeavesTheOtherPending()
    {
        var fixture = new Fixture();
        var whatsApp = fixture.AddGroup("Bay Area Soccer");
        var other = fixture.AddGroup("Sunday League");
        fixture.PickupPalLists(whatsApp);

        var result = await fixture.RequestHandler().HandleAsync(
            new RequestGroupMembershipsCommand([whatsApp.Id, other.Id]));

        fixture.Added.Should().HaveCount(2);
        var approved = fixture.Added.Single(row => row.GroupChatId == whatsApp.Id);
        approved.Status.Should().Be(GroupMembershipStatus.Approved);
        approved.Source.Should().Be(GroupMembershipSource.WhatsApp);
        approved.ApprovedAtUtc.Should().Be(NowUtc);
        approved.IsPrimary.Should().BeTrue();
        var pending = fixture.Added.Single(row => row.GroupChatId == other.Id);
        pending.Status.Should().Be(GroupMembershipStatus.Pending);
        pending.Source.Should().Be(GroupMembershipSource.Request);
        pending.RequestedAtUtc.Should().Be(NowUtc);
        pending.IsPrimary.Should().BeFalse();
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.HasApprovedGroup.Should().BeTrue();
        result.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task RequestMemberships_WhenRowsAlreadyPendingOrApproved_IsIdempotent()
    {
        var fixture = new Fixture();
        var approvedGroup = fixture.AddGroup("Bay Area Soccer");
        var pendingGroup = fixture.AddGroup("Sunday League");
        fixture.ExistingRow(approvedGroup, GroupMembershipStatus.Approved);
        fixture.ExistingRow(pendingGroup, GroupMembershipStatus.Pending);

        await fixture.RequestHandler().HandleAsync(new RequestGroupMembershipsCommand([approvedGroup.Id, pendingGroup.Id]));

        fixture.Added.Should().BeEmpty();
        fixture.Links.Verify(x => x.Update(It.IsAny<PlayerGroupLink>()), Times.Never);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestMemberships_WhenPreviouslyRemoved_ReactivatesTheSameRowAsPending()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        var removed = fixture.ExistingRow(group, GroupMembershipStatus.Removed);
        removed.RemovedAtUtc = NowUtc.AddDays(-3);
        removed.RemovedByPlayerProfileId = Guid.NewGuid();

        await fixture.RequestHandler().HandleAsync(new RequestGroupMembershipsCommand([group.Id]));

        fixture.Added.Should().BeEmpty("a removed pair reuses its row instead of creating a second one");
        removed.Status.Should().Be(GroupMembershipStatus.Pending);
        removed.RequestedAtUtc.Should().Be(NowUtc);
        removed.RemovedAtUtc.Should().BeNull();
        removed.RemovedByPlayerProfileId.Should().BeNull();
        fixture.Links.Verify(x => x.Update(removed), Times.Once);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestMemberships_WhenPickupPalReadFails_LeavesRequestPending()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.GroupClient
            .Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        await fixture.RequestHandler().HandleAsync(new RequestGroupMembershipsCommand([group.Id]));

        fixture.Added.Should().ContainSingle().Which.Status.Should().Be(GroupMembershipStatus.Pending);
    }

    [Fact]
    public async Task RequestMemberships_WhenGroupUnknown_ThrowsNotFound()
    {
        var fixture = new Fixture();

        var act = async () => await fixture.RequestHandler().HandleAsync(new RequestGroupMembershipsCommand([Guid.NewGuid()]));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }

    [Fact]
    public async Task RequestMemberships_WhenNoGroupIds_FailsValidation()
    {
        var fixture = new Fixture();

        var act = async () => await fixture.RequestHandler().HandleAsync(new RequestGroupMembershipsCommand([]));

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----- Leave -----

    [Fact]
    public async Task LeaveGroup_WhenApproved_MarksRemovedByThePlayer()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        var row = fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin, primary: true);

        await fixture.LeaveHandler().HandleAsync(new LeaveGroupCommand(group.Id));

        row.Status.Should().Be(GroupMembershipStatus.Removed);
        row.RemovedAtUtc.Should().Be(NowUtc);
        row.RemovedByPlayerProfileId.Should().Be(fixture.Profile.Id);
        row.Role.Should().Be(GroupMemberRole.Member, "leaving also gives up the admin role");
        row.IsPrimary.Should().BeFalse();
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveGroup_WhenNoRow_ThrowsNotFound()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");

        var act = async () => await fixture.LeaveHandler().HandleAsync(new LeaveGroupCommand(group.Id));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }

    // ----- Approve / decline / remove authorization matrix -----

    [Theory]
    [InlineData(false, false, false)] // ordinary member of the group
    [InlineData(false, true, false)]  // admin of a different group only
    public async Task ReviewMember_WhenCallerIsNotAdminOfThatGroupNorOwner_Forbids(bool isOwner, bool adminElsewhere, bool _)
    {
        var fixture = new Fixture(isOwner);
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved);
        if (adminElsewhere)
        {
            fixture.ExistingRow(fixture.AddGroup("Elsewhere"), GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        }

        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Pending);

        var act = async () => await fixture.ReviewHandler().HandleAsync(
            new ReviewGroupMemberCommand(group.Id, target.PlayerProfileId, GroupMemberReview.Approve));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
        target.Status.Should().Be(GroupMembershipStatus.Pending);
    }

    [Theory]
    [InlineData(true, false)]  // super admin, no membership at all
    [InlineData(false, true)]  // admin of this group
    public async Task ApproveMember_WhenCallerIsOwnerOrGroupAdmin_ApprovesWithAudit(bool isOwner, bool groupAdmin)
    {
        var fixture = new Fixture(isOwner);
        var group = fixture.AddGroup("Bay Area Soccer");
        if (groupAdmin)
        {
            fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        }

        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Pending);

        var result = await fixture.ReviewHandler().HandleAsync(
            new ReviewGroupMemberCommand(group.Id, target.PlayerProfileId, GroupMemberReview.Approve));

        target.Status.Should().Be(GroupMembershipStatus.Approved);
        target.ApprovedAtUtc.Should().Be(NowUtc);
        target.ApprovedByPlayerProfileId.Should().Be(fixture.Profile.Id);
        target.Source.Should().Be(GroupMembershipSource.Request, "approval keeps how the row came to exist");
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.CanManageMembers.Should().BeTrue();
        result.CanAppointAdmins.Should().Be(isOwner);
    }

    [Fact]
    public async Task DeclineMember_WhenPending_MarksDeclinedByTheAdmin()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Pending);

        await fixture.ReviewHandler().HandleAsync(
            new ReviewGroupMemberCommand(group.Id, target.PlayerProfileId, GroupMemberReview.Decline));

        target.Status.Should().Be(GroupMembershipStatus.Declined);
        target.RemovedAtUtc.Should().Be(NowUtc);
        target.RemovedByPlayerProfileId.Should().Be(fixture.Profile.Id);
    }

    [Fact]
    public async Task RemoveMember_WhenApproved_MarksRemovedByTheAdmin()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Approved);

        await fixture.ReviewHandler().HandleAsync(
            new ReviewGroupMemberCommand(group.Id, target.PlayerProfileId, GroupMemberReview.Remove));

        target.Status.Should().Be(GroupMembershipStatus.Removed);
        target.RemovedByPlayerProfileId.Should().Be(fixture.Profile.Id);
    }

    [Fact]
    public async Task RemoveMember_WhenTargetIsGroupAdminAndCallerIsNotOwner_Forbids()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);

        var act = async () => await fixture.ReviewHandler().HandleAsync(
            new ReviewGroupMemberCommand(group.Id, target.PlayerProfileId, GroupMemberReview.Remove));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    [Fact]
    public async Task ApproveMember_WhenRowIsNotPending_Conflicts()
    {
        var fixture = new Fixture(isOwner: true);
        var group = fixture.AddGroup("Bay Area Soccer");
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Removed);

        var act = async () => await fixture.ReviewHandler().HandleAsync(
            new ReviewGroupMemberCommand(group.Id, target.PlayerProfileId, GroupMemberReview.Approve));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    [Fact]
    public async Task GetGroupMembers_WhenGroupAdmin_SplitsPendingFromMembersAdminsFirst()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        fixture.Links
            .Setup(x => x.ListGroupMembersAsync(group.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new GroupMemberReadModel(Guid.NewGuid(), "Ada Lovelace", GroupMembershipStatus.Approved, GroupMemberRole.Member, GroupMembershipSource.WhatsApp, NowUtc, NowUtc),
                new GroupMemberReadModel(Guid.NewGuid(), "Bob Builder", GroupMembershipStatus.Pending, GroupMemberRole.Member, GroupMembershipSource.Request, NowUtc, null),
                new GroupMemberReadModel(fixture.Profile.Id, "Cara Admin", GroupMembershipStatus.Approved, GroupMemberRole.Admin, GroupMembershipSource.WhatsApp, NowUtc, NowUtc),
                new GroupMemberReadModel(Guid.NewGuid(), "Dan Removed", GroupMembershipStatus.Removed, GroupMemberRole.Member, GroupMembershipSource.Request, NowUtc, null),
            ]);

        var result = await fixture.MembersHandler().HandleAsync(new GetGroupMembersQuery(group.Id));

        result.Pending.Should().ContainSingle().Which.DisplayName.Should().Be("Bob Builder");
        result.Members.Select(member => member.DisplayName).Should().Equal("Cara Admin", "Ada Lovelace");
        result.Members[0].Initials.Should().Be("CA");
        result.CanAppointAdmins.Should().BeFalse();
    }

    [Fact]
    public async Task GetGroupMembers_WhenOrdinaryMember_Forbids()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved);

        var act = async () => await fixture.MembersHandler().HandleAsync(new GetGroupMembersQuery(group.Id));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    // ----- Super-admin-only actions -----

    [Fact]
    public async Task SetGroupAdmin_WhenOwnerAndMemberApproved_AppointsAdmin()
    {
        var fixture = new Fixture(isOwner: true);
        var group = fixture.AddGroup("Bay Area Soccer");
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Approved);

        await fixture.SetAdminHandler().HandleAsync(new SetGroupAdminCommand(group.Id, target.PlayerProfileId, IsAdmin: true));

        target.Role.Should().Be(GroupMemberRole.Admin);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetGroupAdmin_WhenMemberOnlyPending_Conflicts()
    {
        var fixture = new Fixture(isOwner: true);
        var group = fixture.AddGroup("Bay Area Soccer");
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Pending);

        var act = async () => await fixture.SetAdminHandler().HandleAsync(new SetGroupAdminCommand(group.Id, target.PlayerProfileId, IsAdmin: true));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        target.Role.Should().Be(GroupMemberRole.Member);
    }

    [Fact]
    public async Task SetGroupAdmin_WhenCallerIsGroupAdminButNotOwner_Forbids()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Approved);

        var act = async () => await fixture.SetAdminHandler().HandleAsync(new SetGroupAdminCommand(group.Id, target.PlayerProfileId, IsAdmin: true));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    [Fact]
    public async Task AddGroupMember_WhenOwner_AddsApprovedSuperAdminRow()
    {
        var fixture = new Fixture(isOwner: true);
        var group = fixture.AddGroup("Bay Area Soccer");
        var known = fixture.KnownPlayer("Known Player");

        await fixture.AddMemberHandler().HandleAsync(new AddGroupMemberCommand(group.Id, known.Id));

        var added = fixture.Added.Should().ContainSingle().Subject;
        added.PlayerProfileId.Should().Be(known.Id);
        added.Status.Should().Be(GroupMembershipStatus.Approved);
        added.Source.Should().Be(GroupMembershipSource.SuperAdmin);
        added.ApprovedByPlayerProfileId.Should().Be(fixture.Profile.Id);
        added.ApprovedAtUtc.Should().Be(NowUtc);
    }

    [Fact]
    public async Task AddGroupMember_WhenPlayerAlreadyPending_ApprovesExistingRow()
    {
        var fixture = new Fixture(isOwner: true);
        var group = fixture.AddGroup("Bay Area Soccer");
        var target = fixture.OtherPlayerRow(group, GroupMembershipStatus.Pending);
        fixture.KnownPlayer("Pending Player", target.PlayerProfileId);

        await fixture.AddMemberHandler().HandleAsync(new AddGroupMemberCommand(group.Id, target.PlayerProfileId));

        fixture.Added.Should().BeEmpty();
        target.Status.Should().Be(GroupMembershipStatus.Approved);
        target.Source.Should().Be(GroupMembershipSource.SuperAdmin);
    }

    [Fact]
    public async Task AddGroupMember_WhenCallerIsGroupAdminButNotOwner_Forbids()
    {
        var fixture = new Fixture();
        var group = fixture.AddGroup("Bay Area Soccer");
        fixture.ExistingRow(group, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        var known = fixture.KnownPlayer("Known Player");

        var act = async () => await fixture.AddMemberHandler().HandleAsync(new AddGroupMemberCommand(group.Id, known.Id));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
        fixture.Added.Should().BeEmpty();
    }

    // ----- Catalog -----

    [Fact]
    public async Task GetGroupCatalog_ShowsPendingCountsOnlyForGroupsTheCallerManages()
    {
        var fixture = new Fixture();
        var managed = fixture.AddGroup("Bay Area Soccer");
        var other = fixture.AddGroup("Sunday League");
        fixture.ExistingRow(managed, GroupMembershipStatus.Approved, role: GroupMemberRole.Admin);
        fixture.ExistingRow(other, GroupMembershipStatus.Pending);
        fixture.Links
            .Setup(x => x.CountByGroupAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, GroupMembershipCounts>
            {
                [managed.Id] = new(12, 3),
                [other.Id] = new(30, 5),
            });

        var result = await fixture.CatalogHandler().HandleAsync(new GetGroupCatalogQuery());

        var managedEntry = result.Groups.Single(group => group.GroupChatId == managed.Id);
        managedEntry.MemberCount.Should().Be(12);
        managedEntry.PendingRequestCount.Should().Be(3);
        managedEntry.Status.Should().Be(GroupMembershipStatus.Approved);
        managedEntry.Role.Should().Be(GroupMemberRole.Admin);
        var otherEntry = result.Groups.Single(group => group.GroupChatId == other.Id);
        otherEntry.PendingRequestCount.Should().Be(0, "pending counts are only shown to those who can act on them");
        otherEntry.Status.Should().Be(GroupMembershipStatus.Pending);
    }

    // ----- Search -----

    [Theory]
    [InlineData("a")]
    [InlineData("  ")]
    [InlineData("someone@example.com")]
    [InlineData("+1 516 555")]
    [InlineData("5165550")]
    public async Task SearchPlayers_WhenQueryIsShortOrLooksLikePersonalIdentifier_FailsValidation(string query)
    {
        var fixture = new Fixture(isOwner: true);

        var act = async () => await fixture.SearchHandler().HandleAsync(new SearchPlayersQuery(query));

        await act.Should().ThrowAsync<ValidationException>();
        fixture.Profiles.Verify(
            x => x.SearchByDisplayNameAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchPlayers_WhenOwner_ReturnsMaskedPhoneOnly()
    {
        var fixture = new Fixture(isOwner: true);
        fixture.Profiles
            .Setup(x => x.SearchByDisplayNameAsync("AD", SearchPlayersQueryHandler.MaxResults, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerProfile { Id = Guid.NewGuid(), DisplayName = "Ada Lovelace", MaskedPhoneNumber = "+******1234", PhoneNumberHash = "hash" }]);

        var result = await fixture.SearchHandler().HandleAsync(new SearchPlayersQuery(" ad "));

        var player = result.Should().ContainSingle().Subject;
        player.DisplayName.Should().Be("Ada Lovelace");
        player.Initials.Should().Be("AL");
        player.MaskedPhone.Should().Be("+******1234");
    }

    [Fact]
    public async Task SearchPlayers_WhenCallerAdministersNoGroup_Forbids()
    {
        var fixture = new Fixture();
        fixture.ExistingRow(fixture.AddGroup("Bay Area Soccer"), GroupMembershipStatus.Approved);

        var act = async () => await fixture.SearchHandler().HandleAsync(new SearchPlayersQuery("ada"));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    private sealed class Fixture
    {
        private readonly Guid identityUserId = Guid.NewGuid();
        private readonly List<GroupChat> groups = [];
        private readonly List<PlayerGroupLink> rows = [];
        private readonly Mock<ICurrentUser> currentUser = new();
        private readonly Mock<IGroupChatRepository> groupChats = new();

        public Fixture(bool isOwner = false)
        {
            Profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Cara Admin", PickupPalUserId = PickupPalUserId };
            currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
            currentUser.Setup(x => x.IsInRole("Owner")).Returns(isOwner);
            Profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(Profile);
            groupChats.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => groups.SingleOrDefault(group => group.Id == id));
            groupChats.Setup(x => x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => groups.Where(group => ids.Contains(group.Id)).ToArray());
            groupChats.Setup(x => x.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => groups.ToArray());
            GroupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Links.Setup(x => x.ListByPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid playerProfileId, CancellationToken _) => rows.Where(row => row.PlayerProfileId == playerProfileId).ToArray());
            Links.Setup(x => x.ListApprovedByPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid playerProfileId, CancellationToken _) => rows
                    .Where(row => row.PlayerProfileId == playerProfileId && row.Status == GroupMembershipStatus.Approved)
                    .ToArray());
            Links.Setup(x => x.FindLinkAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid playerProfileId, Guid groupChatId, CancellationToken _) => rows.SingleOrDefault(row =>
                    row.PlayerProfileId == playerProfileId && row.GroupChatId == groupChatId && row.Status == GroupMembershipStatus.Approved));
            Links.Setup(x => x.FindMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid playerProfileId, Guid groupChatId, CancellationToken _) => rows.SingleOrDefault(row =>
                    row.PlayerProfileId == playerProfileId && row.GroupChatId == groupChatId));
            Links.Setup(x => x.AddAsync(It.IsAny<PlayerGroupLink>(), It.IsAny<CancellationToken>()))
                .Callback<PlayerGroupLink, CancellationToken>((row, _) => Added.Add(row))
                .Returns(Task.CompletedTask);
            Links.Setup(x => x.ListPlayerMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid playerProfileId, CancellationToken _) => rows.Concat(Added)
                    .Where(row => row.PlayerProfileId == playerProfileId)
                    .Select(row => new PlayerMembershipReadModel(
                        row.GroupChatId,
                        groups.Single(group => group.Id == row.GroupChatId).GroupName,
                        row.Status, row.Role, row.Source, row.RequestedAtUtc, row.ApprovedAtUtc))
                    .ToArray());
            Links.Setup(x => x.ListGroupMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Links.Setup(x => x.CountByGroupAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, GroupMembershipCounts>());
        }

        public PlayerProfile Profile { get; }
        public Mock<IPlayerProfileRepository> Profiles { get; } = new();
        public Mock<IPlayerGroupLinkRepository> Links { get; } = new();
        public Mock<IPickupPalGroupClient> GroupClient { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<PlayerGroupLink> Added { get; } = [];

        public GroupChat AddGroup(string name)
        {
            var group = new GroupChat { Id = Guid.NewGuid(), ExternalId = $"{name}@g.us", GroupName = name };
            groups.Add(group);
            return group;
        }

        public void PickupPalLists(params GroupChat[] whatsAppGroups) =>
            GroupClient.Setup(x => x.GetLinkedGroupsAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(whatsAppGroups.Select(group => new PickupPalGroupChat(group.ExternalId, group.GroupName, null, "SUBSCRIBED", 1, null)).ToArray());

        public PlayerGroupLink ExistingRow(GroupChat group, GroupMembershipStatus status, GroupMemberRole role = GroupMemberRole.Member, bool primary = false) =>
            Row(Profile.Id, group, status, role, primary);

        public PlayerGroupLink OtherPlayerRow(GroupChat group, GroupMembershipStatus status, GroupMemberRole role = GroupMemberRole.Member) =>
            Row(Guid.NewGuid(), group, status, role, primary: false);

        public PlayerProfile KnownPlayer(string name, Guid? id = null)
        {
            var player = new PlayerProfile { Id = id ?? Guid.NewGuid(), DisplayName = name };
            Profiles.Setup(x => x.FindProfileAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
            return player;
        }

        public RequestGroupMembershipsCommandHandler RequestHandler() =>
            new(new RequestGroupMembershipsCommandValidator(), currentUser.Object, Profiles.Object, groupChats.Object, Links.Object, Service());

        public LeaveGroupCommandHandler LeaveHandler() => new(currentUser.Object, Profiles.Object, Links.Object, Service());

        public GetGroupMembersQueryHandler MembersHandler() => new(currentUser.Object, Profiles.Object, groupChats.Object, Links.Object);

        public ReviewGroupMemberCommandHandler ReviewHandler() =>
            new(currentUser.Object, Profiles.Object, groupChats.Object, Links.Object, Service());

        public AddGroupMemberCommandHandler AddMemberHandler() =>
            new(currentUser.Object, Profiles.Object, groupChats.Object, Links.Object, Service());

        public SetGroupAdminCommandHandler SetAdminHandler() =>
            new(currentUser.Object, Profiles.Object, groupChats.Object, Links.Object, Service());

        public GetGroupCatalogQueryHandler CatalogHandler() => new(currentUser.Object, Profiles.Object, groupChats.Object, Links.Object);

        public SearchPlayersQueryHandler SearchHandler() =>
            new(new SearchPlayersQueryValidator(), currentUser.Object, Profiles.Object, Links.Object);

        private GroupMembershipService Service()
        {
            var clock = new Mock<IClock>();
            clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
            return new GroupMembershipService(clock.Object, GroupClient.Object, Links.Object, UnitOfWork.Object);
        }

        private PlayerGroupLink Row(Guid playerProfileId, GroupChat group, GroupMembershipStatus status, GroupMemberRole role, bool primary)
        {
            var row = new PlayerGroupLink
            {
                Id = Guid.NewGuid(),
                PlayerProfileId = playerProfileId,
                GroupChatId = group.Id,
                Status = status,
                Role = role,
                IsPrimary = primary,
                Source = GroupMembershipSource.Request,
                RequestedAtUtc = NowUtc.AddDays(-1),
                ApprovedAtUtc = status == GroupMembershipStatus.Approved ? NowUtc.AddDays(-1) : null,
            };
            rows.Add(row);
            return row;
        }
    }
}
