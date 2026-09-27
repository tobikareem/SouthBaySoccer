using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

/// <summary>
/// The session admin handlers commit locally first and then hand the session to the Pickup Pal
/// sync; these tests pin the order and the group association, not the push itself (see
/// <see cref="SessionPickupPalSyncServiceTests"/>).
/// </summary>
public sealed class SessionAdminPickupPalHandlerTests
{
    private static readonly DateTime StartUtc = new(2026, 9, 19, 2, 40, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Publish_WhenDraftIsPublished_CommitsThenSyncsAndReportsTheGroup()
    {
        var context = new TestContext(status: SessionStatus.Draft);
        var handler = new PublishSessionCommandHandler(
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        var model = await handler.HandleAsync(context.Session.Id);

        context.Session.Status.Should().Be(SessionStatus.Published);
        context.Calls.Should().Equal("save", "sync");
        model.GroupChatId.Should().Be(context.Group.Id);
        model.GroupName.Should().Be("South Bay");
    }

    [Fact]
    public async Task Publish_WhenAlreadyPublished_DoesNotSyncAgain()
    {
        var context = new TestContext(status: SessionStatus.Published);
        var handler = new PublishSessionCommandHandler(
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        await handler.HandleAsync(context.Session.Id);

        context.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_WhenNoGroupIsSent_KeepsTheExistingGroupAndSyncsAfterSave()
    {
        var context = new TestContext(status: SessionStatus.Published);
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            context.SeasonRepository.Object,
            context.VenueRepository.Object,
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        await handler.HandleAsync(new UpdateSessionAdminCommand(
            context.Session.Id,
            context.Venue.Id,
            context.Venue.Name,
            "7v7",
            16,
            2,
            StartUtc,
            StartUtc.AddMinutes(-10),
            StartUtc,
            StartUtc.AddHours(-1)));

        context.Session.Capacity.Should().Be(16);
        context.Session.GroupChatId.Should().Be(context.Group.Id);
        context.Calls.Should().Equal("save", "sync");
    }

    [Fact]
    public async Task Update_WhenAnotherGroupIsSent_ReplacesTheAssociation()
    {
        var context = new TestContext(status: SessionStatus.Draft);
        var other = context.AddGroup("Other group");
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            context.SeasonRepository.Object,
            context.VenueRepository.Object,
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        var model = await handler.HandleAsync(new UpdateSessionAdminCommand(
            context.Session.Id,
            context.Venue.Id,
            context.Venue.Name,
            "7v7",
            14,
            2,
            StartUtc,
            StartUtc.AddMinutes(-10),
            StartUtc,
            StartUtc.AddHours(-1),
            other.Id));

        context.Session.GroupChatId.Should().Be(other.Id);
        model.GroupName.Should().Be("Other group");
    }

    [Fact]
    public async Task Update_WhenTheGameExistsAndAnotherGroupIsSent_IsRejected()
    {
        var context = new TestContext(status: SessionStatus.Published);
        context.Session.PickupPalGameId = "game-9";
        context.Session.PickupPalOrigin = PickupPalOrigin.CreatedByApp;
        var other = context.AddGroup("Other group");
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            context.SeasonRepository.Object,
            context.VenueRepository.Object,
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        var act = () => handler.HandleAsync(new UpdateSessionAdminCommand(
            context.Session.Id,
            context.Venue.Id,
            context.Venue.Name,
            "7v7",
            14,
            2,
            StartUtc,
            StartUtc.AddMinutes(-10),
            StartUtc,
            StartUtc.AddHours(-1),
            other.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("The group cannot change once the Pickup Pal game exists.");
        context.Session.GroupChatId.Should().Be(context.Group.Id);
        context.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_WhenTheGameExistsAndTheSameGroupIsSent_Proceeds()
    {
        var context = new TestContext(status: SessionStatus.Published);
        context.Session.PickupPalGameId = "game-9";
        context.Session.PickupPalOrigin = PickupPalOrigin.CreatedByApp;
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            context.SeasonRepository.Object,
            context.VenueRepository.Object,
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        await handler.HandleAsync(new UpdateSessionAdminCommand(
            context.Session.Id,
            context.Venue.Id,
            context.Venue.Name,
            "7v7",
            16,
            2,
            StartUtc,
            StartUtc.AddMinutes(-10),
            StartUtc,
            StartUtc.AddHours(-1),
            context.Group.Id));

        context.Session.Capacity.Should().Be(16);
        context.Calls.Should().Equal("save", "sync");
    }

    [Fact]
    public async Task Cancel_WhenSessionIsCanceled_CommitsThenSyncsAndReportsTheGroup()
    {
        var context = new TestContext(status: SessionStatus.Published);
        var handler = new CancelSessionCommandHandler(
            context.SessionRepository.Object,
            context.Resolver,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        var model = await handler.HandleAsync(new CancelSessionCommand(context.Session.Id, "Rain"));

        context.Session.Status.Should().Be(SessionStatus.Canceled);
        context.Calls.Should().Equal("save", "sync");
        model.GroupName.Should().Be("South Bay");
    }

    [Fact]
    public async Task Delete_WhenSessionIsDeleted_CommitsThenSyncs()
    {
        var context = new TestContext(status: SessionStatus.Published);
        var handler = new DeleteSessionCommandHandler(
            context.SessionRepository.Object,
            context.SyncService.Object,
            context.UnitOfWork.Object);

        await handler.HandleAsync(new DeleteSessionCommand(context.Session.Id));

        context.SessionRepository.Verify(x => x.SoftDelete(context.Session), Times.Once);
        context.Calls.Should().Equal("save", "sync");
    }

    [Fact]
    public async Task CreateDraft_WhenNoGroupIsSent_DefaultsToTheAdminsOnlyGroupAndNeverPushes()
    {
        var context = new TestContext(status: SessionStatus.Draft);
        Session? added = null;
        context.SessionRepository
            .Setup(x => x.AddAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .Callback<Session, CancellationToken>((session, _) => added = session)
            .Returns(Task.CompletedTask);
        var handler = new CreateSessionDraftCommandHandler(
            new CreateSessionCommandValidator(),
            context.SeasonRepository.Object,
            context.VenueRepository.Object,
            context.SessionRepository.Object,
            context.Resolver,
            context.UnitOfWork.Object);

        var model = await handler.HandleAsync(new CreateSessionDraftCommand(
            context.Venue.Id,
            context.Venue.Name,
            "7v7",
            14,
            2,
            StartUtc.AddDays(7),
            StartUtc.AddDays(7).AddMinutes(-10),
            StartUtc.AddDays(7),
            StartUtc.AddDays(7).AddHours(-1)));

        added.Should().NotBeNull();
        added!.GroupChatId.Should().Be(context.Group.Id);
        added.Status.Should().Be(SessionStatus.Draft);
        model.GroupName.Should().Be("South Bay");
        context.Calls.Should().Equal("save");
    }

    private sealed class TestContext
    {
        public Session Session { get; }

        public GroupChat Group { get; }

        public Venue Venue { get; }

        public Mock<ISessionRepository> SessionRepository { get; } = new();

        public Mock<ISeasonRepository> SeasonRepository { get; } = new();

        public Mock<IVenueRepository> VenueRepository { get; } = new();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Mock<ISessionPickupPalSyncService> SyncService { get; } = new();

        public SessionGroupResolver Resolver { get; }

        public List<string> Calls { get; } = [];

        private readonly Mock<IGroupChatRepository> groupRepository = new();

        public TestContext(SessionStatus status)
        {
            Group = new GroupChat { Id = Guid.NewGuid(), ExternalId = "1408-1520@g.us", GroupName = "South Bay" };
            groupRepository.Setup(x => x.GetByIdAsync(Group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Group);
            Venue = new Venue { Id = Guid.NewGuid(), Name = "Caribbean Park", Locality = "Sunnyvale" };
            Session = new Session
            {
                Id = Guid.NewGuid(),
                VenueId = Venue.Id,
                Title = "Caribbean Park - Friday pickup",
                Format = "7v7",
                Capacity = 14,
                TeamCount = 2,
                StartsAtUtc = StartUtc,
                CheckInOpensAtUtc = StartUtc.AddMinutes(-10),
                CheckInClosesAtUtc = StartUtc,
                RsvpDeadlineUtc = StartUtc.AddHours(-1),
                Status = status,
                GroupChatId = Group.Id,
            };
            SessionRepository.Setup(x => x.GetByIdAsync(Session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Session);
            SeasonRepository
                .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Season { Id = Guid.NewGuid(), Name = "2026", StartsAtUtc = StartUtc.AddMonths(-1), EndsAtUtc = StartUtc.AddMonths(2) }]);
            VenueRepository.Setup(x => x.GetByIdAsync(Venue.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Venue);
            VenueRepository.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Venue]);
            UnitOfWork
                .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => Calls.Add("save"))
                .ReturnsAsync(1);
            SyncService
                .Setup(x => x.SyncAfterLocalWriteAsync(Session.Id, It.IsAny<CancellationToken>()))
                .Callback(() => Calls.Add("sync"))
                .ReturnsAsync(PickupPalSyncStatus.Synced);
            SyncService
                .Setup(x => x.SyncAfterLocalWriteAsync(It.Is<Guid>(id => id != Session.Id), It.IsAny<CancellationToken>()))
                .Callback(() => Calls.Add("sync"))
                .ReturnsAsync(PickupPalSyncStatus.NotApplicable);

            var identityUserId = Guid.NewGuid();
            var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Admin", PickupPalUserId = "pp-admin-1" };
            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
            var profileRepository = new Mock<IPlayerProfileRepository>();
            profileRepository.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
            var linkRepository = new Mock<IPlayerGroupLinkRepository>();
            linkRepository
                .Setup(x => x.ListApprovedByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PlayerGroupLink { Id = Guid.NewGuid(), PlayerProfileId = profile.Id, GroupChatId = Group.Id }]);
            Resolver = new SessionGroupResolver(currentUser.Object, profileRepository.Object, linkRepository.Object, groupRepository.Object);
        }

        public GroupChat AddGroup(string name)
        {
            var group = new GroupChat { Id = Guid.NewGuid(), ExternalId = $"{name}@g.us", GroupName = name };
            groupRepository.Setup(x => x.GetByIdAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
            return group;
        }
    }
}
