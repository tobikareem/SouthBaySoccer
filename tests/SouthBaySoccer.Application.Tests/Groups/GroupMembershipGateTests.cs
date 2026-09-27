using System.Text.Json;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Groups;

public sealed class GroupMembershipGateTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureCanJoin_WhenSessionHasNoGroup_AllowsWithoutReadingMemberships()
    {
        var links = new Mock<IPlayerGroupLinkRepository>(MockBehavior.Strict);
        var gate = new GroupMembershipGate(links.Object, Mock.Of<IGroupChatRepository>());

        var act = async () => await gate.EnsureCanJoinAsync(Session(groupChatId: null), Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureCanJoin_WhenApprovedMember_Allows()
    {
        var groupId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ExistsApprovedAsync(playerId, groupId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var gate = new GroupMembershipGate(links.Object, Mock.Of<IGroupChatRepository>());

        var act = async () => await gate.EnsureCanJoinAsync(Session(groupId), playerId);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(PickupPalOrigin.Imported, null)]
    [InlineData(PickupPalOrigin.None, "pickuppal:legacy")]
    public async Task EnsureCanJoin_WhenImportedGroupUnresolved_Rejects(PickupPalOrigin origin, string? occurrenceKey)
    {
        var session = Session(null);
        session.PickupPalOrigin = origin;
        session.OccurrenceKey = occurrenceKey;
        var gate = new GroupMembershipGate(Mock.Of<IPlayerGroupLinkRepository>(), Mock.Of<IGroupChatRepository>());

        var act = () => gate.EnsureCanJoinAsync(session, Guid.NewGuid());

        await act.Should().ThrowAsync<GroupMembershipRequiredException>();
    }

    [Theory]
    [InlineData(PickupPalOrigin.Imported, null, false)]
    [InlineData(PickupPalOrigin.None, "pickuppal:legacy", false)]
    [InlineData(PickupPalOrigin.CreatedByApp, "pickuppal:app", true)]
    [InlineData(PickupPalOrigin.None, null, true)]
    public async Task ResolveAccess_WhenNoGroup_OnlyOpensAppSessions(PickupPalOrigin origin, string? occurrenceKey, bool canJoin)
    {
        var session = Session(null);
        session.PickupPalOrigin = origin;
        session.OccurrenceKey = occurrenceKey;
        var gate = new GroupMembershipGate(Mock.Of<IPlayerGroupLinkRepository>(), Mock.Of<IGroupChatRepository>());

        var result = await gate.ResolveAccessAsync([session], Guid.NewGuid());

        result[session.Id].CanJoin.Should().Be(canJoin);
    }

    [Theory]
    [InlineData(GroupMembershipStatus.Pending)]
    [InlineData(GroupMembershipStatus.Declined)]
    [InlineData(GroupMembershipStatus.Removed)]
    public async Task EnsureCanJoin_WhenNotApproved_ThrowsWithGroupNameOnly(GroupMembershipStatus status)
    {
        _ = status; // ExistsApprovedAsync is false for every non-approved status.
        var group = new GroupChat { Id = Guid.NewGuid(), ExternalId = "secret-id@g.us", GroupName = "Bay Area Soccer" };
        var playerId = Guid.NewGuid();
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ExistsApprovedAsync(playerId, group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var groups = new Mock<IGroupChatRepository>();
        groups.Setup(x => x.GetByIdAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        var gate = new GroupMembershipGate(links.Object, groups.Object);

        var act = async () => await gate.EnsureCanJoinAsync(Session(group.Id), playerId);

        var thrown = await act.Should().ThrowAsync<GroupMembershipRequiredException>();
        thrown.Which.GroupName.Should().Be("Bay Area Soccer");
        thrown.Which.Message.Should().Contain("Bay Area Soccer").And.NotContain("secret-id");
    }

    [Fact]
    public async Task ResolveAccess_ProjectsStatusAndCanJoinPerSession()
    {
        var playerId = Guid.NewGuid();
        var approvedGroup = new GroupChat { Id = Guid.NewGuid(), ExternalId = "a@g.us", GroupName = "Approved FC" };
        var pendingGroup = new GroupChat { Id = Guid.NewGuid(), ExternalId = "p@g.us", GroupName = "Pending FC" };
        var strangerGroup = new GroupChat { Id = Guid.NewGuid(), ExternalId = "s@g.us", GroupName = "Stranger FC" };
        var sessions = new[]
        {
            Session(approvedGroup.Id), Session(pendingGroup.Id), Session(strangerGroup.Id), Session(null),
        };
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.ListByPlayerAsync(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PlayerGroupLink { PlayerProfileId = playerId, GroupChatId = approvedGroup.Id, Status = GroupMembershipStatus.Approved },
            new PlayerGroupLink { PlayerProfileId = playerId, GroupChatId = pendingGroup.Id, Status = GroupMembershipStatus.Pending },
        ]);
        var groups = new Mock<IGroupChatRepository>();
        groups.Setup(x => x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([approvedGroup, pendingGroup, strangerGroup]);
        var gate = new GroupMembershipGate(links.Object, groups.Object);

        var access = await gate.ResolveAccessAsync(sessions, playerId);

        access[sessions[0].Id].Should().Be(new SessionGroupAccess(approvedGroup.Id, "Approved FC", GroupMembershipStatus.Approved, true));
        access[sessions[1].Id].Should().Be(new SessionGroupAccess(pendingGroup.Id, "Pending FC", GroupMembershipStatus.Pending, false));
        access[sessions[2].Id].Should().Be(new SessionGroupAccess(strangerGroup.Id, "Stranger FC", null, false));
        access[sessions[3].Id].Should().Be(SessionGroupAccess.Open);
    }

    [Fact]
    public async Task SubmitRsvp_WhenNotApprovedMember_RejectsBeforeWriting()
    {
        var fixture = new ActorFixture();
        var rsvps = new Mock<IRsvpRepository>(MockBehavior.Strict);
        var handler = new SubmitRsvpCommandHandler(
            fixture.CurrentUser.Object,
            fixture.Clock.Object,
            new SubmitRsvpCommandValidator(),
            fixture.Profiles.Object,
            fixture.Sessions.Object,
            Mock.Of<IPlayerSessionEligibilityService>(),
            rsvps.Object,
            Mock.Of<IRsvpPickupPalSyncService>(),
            fixture.ClosedGate());

        var act = async () => await handler.HandleAsync(new SubmitRsvpCommand(fixture.Session.Id, RsvpStatus.Going));

        await act.Should().ThrowAsync<GroupMembershipRequiredException>();
    }

    [Theory]
    [InlineData(RsvpStatus.Maybe)]
    [InlineData(RsvpStatus.NotGoing)]
    public async Task SubmitRsvp_WhenNonmemberSubmitsOtherIntent_RejectsBeforeWriting(RsvpStatus status)
    {
        var fixture = new ActorFixture();
        var eligibility = new Mock<IPlayerSessionEligibilityService>(MockBehavior.Strict);
        var rsvps = new Mock<IRsvpRepository>(MockBehavior.Strict);
        var handler = new SubmitRsvpCommandHandler(
            fixture.CurrentUser.Object,
            fixture.Clock.Object,
            new SubmitRsvpCommandValidator(),
            fixture.Profiles.Object,
            fixture.Sessions.Object,
            eligibility.Object,
            rsvps.Object,
            Mock.Of<IRsvpPickupPalSyncService>(),
            fixture.ClosedGate());

        var act = async () => await handler.HandleAsync(new SubmitRsvpCommand(fixture.Session.Id, status));

        await act.Should().ThrowAsync<GroupMembershipRequiredException>();
        eligibility.VerifyNoOtherCalls();
        rsvps.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SelfCheckIn_WhenNotApprovedMember_RejectsBeforeReadingAttendance()
    {
        var fixture = new ActorFixture();
        fixture.Session.CheckInOpensAtUtc = NowUtc.AddMinutes(-5);
        fixture.Session.CheckInClosesAtUtc = NowUtc.AddMinutes(5);
        var rsvps = new Mock<IRsvpRepository>(MockBehavior.Strict);
        var handler = new SelfCheckInCommandHandler(
            fixture.CurrentUser.Object,
            fixture.Clock.Object,
            fixture.Profiles.Object,
            fixture.Sessions.Object,
            Mock.Of<IPlayerSessionEligibilityService>(),
            rsvps.Object,
            fixture.ClosedGate());

        var act = async () => await handler.HandleAsync(new SelfCheckInCommand(fixture.Session.Id));

        await act.Should().ThrowAsync<GroupMembershipRequiredException>();
    }

    [Fact]
    public async Task ClaimParticipant_WhenNotApprovedMember_RejectsBeforeTouchingTheRoster()
    {
        var fixture = new ActorFixture();
        var games = new Mock<IPickupPalGameRepository>(MockBehavior.Strict);
        var handler = new ClaimParticipantCommandHandler(
            fixture.CurrentUser.Object,
            fixture.Clock.Object,
            fixture.Profiles.Object,
            fixture.Sessions.Object,
            Mock.Of<IRsvpRepository>(),
            games.Object,
            Mock.Of<IStatsRepository>(),
            Mock.Of<IAuditLogRepository>(),
            Mock.Of<IUnitOfWork>(),
            fixture.ClosedGate());

        var act = async () => await handler.HandleAsync(new ClaimParticipantCommand(fixture.Session.Id, Guid.NewGuid()));

        await act.Should().ThrowAsync<GroupMembershipRequiredException>();
    }

    [Fact]
    public async Task ListUpcomingSessions_ProjectsGroupAccessAndWithholdsWaitlistFromNonMembers()
    {
        var fixture = new ActorFixture();
        var openSession = Session(null);
        var repository = new Mock<ISessionRepository>();
        repository
            .Setup(x => x.ListUpcomingFeedAsync(NowUtc, It.IsAny<int>(), fixture.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                // Full group game the caller is not a member of: visible, not joinable.
                new SessionFeedRecord(fixture.Session, "Field", GoingCount: fixture.Session.Capacity, WaitlistCount: 0, false, false, "Bay Area Soccer"),
                new SessionFeedRecord(openSession, "Field", GoingCount: openSession.Capacity, WaitlistCount: 0, false, false, null),
            ]);
        var handler = new ListUpcomingSessionsQueryHandler(
            fixture.CurrentUser.Object, fixture.Clock.Object, fixture.Profiles.Object, repository.Object, fixture.ClosedGate());

        var feed = await handler.HandleAsync();

        var groupGame = feed.Single(item => item.Session.SessionId == fixture.Session.Id);
        groupGame.CanJoin.Should().BeFalse();
        groupGame.CanJoinWaitlist.Should().BeFalse("the waitlist is a group-scoped action too");
        groupGame.MembershipStatus.Should().Be(GroupMembershipStatus.Pending.ToString());
        groupGame.GroupChatId.Should().Be(fixture.Group.Id);
        groupGame.GroupName.Should().Be("Bay Area Soccer");
        var openGame = feed.Single(item => item.Session.SessionId == openSession.Id);
        openGame.CanJoin.Should().BeTrue();
        openGame.CanJoinWaitlist.Should().BeTrue();
        openGame.MembershipStatus.Should().BeNull();
    }

    [Fact]
    public void PickupPalGame_WhenSerializedForTheSnapshot_OmitsTheGroupExternalId()
    {
        var game = new PickupPalGame("game-1", NowUtc, "Field", 10, "active", "Fire FC", [], GroupExternalId: "secret-group@g.us");

        var json = JsonSerializer.Serialize(game, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContain("secret-group").And.NotContain("groupExternalId");
    }

    private static Session Session(Guid? groupChatId) =>
        new()
        {
            Id = Guid.NewGuid(),
            SeasonId = Guid.NewGuid(),
            VenueId = Guid.NewGuid(),
            Title = "Game",
            Format = "7v7",
            Capacity = 14,
            TeamCount = 2,
            StartsAtUtc = NowUtc.AddHours(3),
            CheckInOpensAtUtc = NowUtc.AddHours(2),
            CheckInClosesAtUtc = NowUtc.AddHours(4),
            RsvpDeadlineUtc = NowUtc.AddHours(2),
            Status = SessionStatus.Published,
            GroupChatId = groupChatId,
        };

    /// <summary>A signed-in player with a Pending row in the session's group, and a real gate over mocked repositories.</summary>
    private sealed class ActorFixture
    {
        public ActorFixture()
        {
            var identityUserId = Guid.NewGuid();
            Profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Pending Pat" };
            Group = new GroupChat { Id = Guid.NewGuid(), ExternalId = "bay@g.us", GroupName = "Bay Area Soccer" };
            Session = GroupMembershipGateTests.Session(Group.Id);
            CurrentUser.SetupGet(x => x.UserId).Returns(identityUserId);
            Clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
            Profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(Profile);
            Sessions.Setup(x => x.GetByIdAsync(Session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Session);
        }

        public PlayerProfile Profile { get; }
        public GroupChat Group { get; }
        public Session Session { get; }
        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IClock> Clock { get; } = new();
        public Mock<IPlayerProfileRepository> Profiles { get; } = new();
        public Mock<ISessionRepository> Sessions { get; } = new();

        public IGroupMembershipGate ClosedGate()
        {
            var links = new Mock<IPlayerGroupLinkRepository>();
            links.Setup(x => x.ExistsApprovedAsync(Profile.Id, Group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            links.Setup(x => x.ListByPlayerAsync(Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = Profile.Id, GroupChatId = Group.Id, Status = GroupMembershipStatus.Pending }]);
            var groups = new Mock<IGroupChatRepository>();
            groups.Setup(x => x.GetByIdAsync(Group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Group);
            groups.Setup(x => x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([Group]);
            return new GroupMembershipGate(links.Object, groups.Object);
        }
    }
}
