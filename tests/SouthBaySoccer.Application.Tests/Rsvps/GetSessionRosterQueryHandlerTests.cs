using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Rsvps;

public sealed class GetSessionRosterQueryHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid CurrentUserId = Guid.NewGuid();
    private static readonly Guid CurrentProfileId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_UnionsLocalPlayersWithImportedParticipants()
    {
        var markId = Guid.NewGuid();
        var topeId = Guid.NewGuid();
        var handler = CreateHandler(
            localGoing: [new RosterMemberRecord(CurrentProfileId, "Tobi Kareem", "Midfielder", false, null)],
            localWaitlist: [new RosterMemberRecord(Guid.NewGuid(), "Mike A.", "Midfielder", false, 2)],
            imported:
            [
                Participant(markId, "Mark A", isWaitlist: false, order: 0),
                Participant(topeId, "tope", isWaitlist: true, order: 1, isGuest: true),
            ]);

        var roster = await handler.HandleAsync(SessionId);

        roster.Going.Select(member => member.DisplayName).Should().Equal("Tobi Kareem", "Mark A");
        roster.Going[0].IsCurrentPlayer.Should().BeTrue();
        roster.Going[1].IsCurrentPlayer.Should().BeFalse();
        roster.Going[1].PlayerProfileId.Should().Be(markId, "imported entries surface their stable row id");
        roster.Waitlist.Select(member => member.DisplayName).Should().Equal("Mike A.", "tope");
        roster.Waitlist.Select(member => member.WaitlistPosition).Should().Equal(1, 2);
        roster.Waitlist[1].IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenSessionUnknown_ThrowsNotFound()
    {
        var handler = CreateHandler([], [], [], sessionExists: false);

        var act = () => handler.HandleAsync(SessionId);

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WhenCallerAnonymous_MarksNoCurrentPlayer()
    {
        var handler = CreateHandler(
            localGoing: [new RosterMemberRecord(CurrentProfileId, "Tobi Kareem", "Midfielder", false, null)],
            localWaitlist: [],
            imported: [],
            authenticated: false);

        var roster = await handler.HandleAsync(SessionId);

        roster.Going.Should().OnlyContain(member => !member.IsCurrentPlayer);
    }

    private static PickupPalGameParticipant Participant(
        Guid id,
        string displayName,
        bool isWaitlist,
        int order,
        bool isGuest = false) =>
        new()
        {
            Id = id,
            SessionId = SessionId,
            PickupPalParticipantId = $"p-{order}",
            DisplayName = displayName,
            IsGuest = isGuest,
            IsWaitlist = isWaitlist,
            DisplayOrder = order,
        };

    private static GetSessionRosterQueryHandler CreateHandler(
        IReadOnlyList<RosterMemberRecord> localGoing,
        IReadOnlyList<RosterMemberRecord> localWaitlist,
        IReadOnlyList<PickupPalGameParticipant> imported,
        bool sessionExists = true,
        bool authenticated = true)
    {
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionExists ? new Session { Id = SessionId } : null);

        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            .Setup(x => x.ListGoingRosterAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localGoing);
        rsvpRepository
            .Setup(x => x.ListActiveWaitlistRosterAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localWaitlist);

        var gameRepository = new Mock<IPickupPalGameRepository>();
        gameRepository
            .Setup(x => x.ListParticipantsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(imported);

        var profileRepository = new Mock<IPlayerProfileRepository>();
        profileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(CurrentUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerProfile { Id = CurrentProfileId, DisplayName = "Tobi Kareem" });

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(authenticated ? CurrentUserId : null);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);

        return new GetSessionRosterQueryHandler(
            sessionRepository.Object,
            rsvpRepository.Object,
            gameRepository.Object,
            profileRepository.Object,
            currentUser.Object);
    }
}
