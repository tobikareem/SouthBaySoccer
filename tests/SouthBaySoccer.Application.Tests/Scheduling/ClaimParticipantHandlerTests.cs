using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class ClaimParticipantHandlerTests
{
    private static readonly Guid IdentityUserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task Claim_WhenEntryIsUnclaimedAndPlayerHasNoSpot_LinksParticipantToTheCaller()
    {
        var actor = Profile("Vic");
        var victor = Participant("victor", playerProfileId: null);
        var repo = GameRepo(victor);
        PickupPalGameParticipant? updated = null;
        repo.Setup(x => x.UpdateParticipant(It.IsAny<PickupPalGameParticipant>()))
            .Callback((PickupPalGameParticipant p) => updated = p);
        var handler = ClaimHandler(actor, repo);

        await handler.HandleAsync(new ClaimParticipantCommand(SessionId, victor.Id));

        updated.Should().NotBeNull();
        updated!.PlayerProfileId.Should().Be(actor.Id, "the claim always links to the caller, never an arbitrary profile");
    }

    [Fact]
    public async Task Claim_WhenEntryAlreadyClaimed_ThrowsConflict()
    {
        var actor = Profile("Vic");
        var taken = Participant("victor", playerProfileId: Guid.NewGuid());
        var handler = ClaimHandler(actor, GameRepo(taken));

        var act = () => handler.HandleAsync(new ClaimParticipantCommand(SessionId, taken.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    [Fact]
    public async Task Claim_WhenCallerAlreadyHasASpotOnTheGame_ThrowsConflict()
    {
        var actor = Profile("Vic");
        var mine = Participant("vic", playerProfileId: actor.Id);
        var other = Participant("victor", playerProfileId: null);
        var handler = ClaimHandler(actor, GameRepo(mine, other));

        var act = () => handler.HandleAsync(new ClaimParticipantCommand(SessionId, other.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    [Fact]
    public async Task Claimables_WhenPlayerNotOnRoster_ListsOnlyUnclaimedEntries()
    {
        var actor = Profile("Vic");
        var realOther = ClaimedProfile("real player");
        var repo = GameRepo(
            Participant("victor", playerProfileId: null),
            Participant("linked player", playerProfileId: realOther.Id));
        var handler = new GetSessionClaimablesQueryHandler(
            CurrentUser().Object, ProfileRepo(actor, realOther).Object, SessionRepo().Object, RsvpRepo().Object, repo.Object);

        var result = await handler.HandleAsync(new GetSessionClaimablesQuery(SessionId));

        result.AlreadyOnRoster.Should().BeFalse();
        result.MyRegisteredName.Should().Be("Vic");
        // A row owned by a signed-in player is never claimable; only the truly unclaimed one shows.
        result.Claimable.Select(c => c.DisplayName).Should().Equal("victor");
    }

    [Fact]
    public async Task Claimables_IncludesRowsLinkedToUnclaimedProfiles()
    {
        var actor = Profile("Vic");
        var duplicate = UnclaimedProfile("victor sanchez46");
        var repo = GameRepo(
            Participant("victor sanchez46", playerProfileId: duplicate.Id),
            Participant("oz", playerProfileId: null));
        var handler = new GetSessionClaimablesQueryHandler(
            CurrentUser().Object, ProfileRepo(actor, duplicate).Object, SessionRepo().Object, RsvpRepo().Object, repo.Object);

        var result = await handler.HandleAsync(new GetSessionClaimablesQuery(SessionId));

        // The import duplicate (no login) is claimable alongside the never-linked row.
        result.Claimable.Select(c => c.DisplayName).Should().BeEquivalentTo("victor sanchez46", "oz");
    }

    [Fact]
    public async Task Claim_WhenRowLinkedToUnclaimedProfile_MergesDuplicateIntoCaller()
    {
        var actor = Profile("Vic");
        var duplicate = UnclaimedProfile("victor46");
        var participant = Participant("victor46", playerProfileId: duplicate.Id);
        var repo = GameRepo(participant);
        var profileRepo = ProfileRepo(actor, duplicate);
        var stats = new Mock<IStatsRepository>();
        var handler = new ClaimParticipantCommandHandler(
            CurrentUser().Object, Clock().Object, profileRepo.Object, SessionRepo().Object,
            RsvpRepo().Object, repo.Object, stats.Object, Mock.Of<IAuditLogRepository>(), Mock.Of<IUnitOfWork>());

        await handler.HandleAsync(new ClaimParticipantCommand(SessionId, participant.Id));

        stats.Verify(x => x.ReassignProfileStatsAsync(duplicate.Id, actor.Id, It.IsAny<CancellationToken>()), Times.Once);
        profileRepo.Verify(x => x.AddProfileMergeAsync(
            It.Is<ProfileMerge>(m => m.SourcePlayerProfileId == duplicate.Id && m.TargetPlayerProfileId == actor.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        duplicate.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Claimables_WhenPlayerAlreadyLinked_ReturnsEmptyAndFlagsOnRoster()
    {
        var actor = Profile("Vic");
        var repo = GameRepo(
            Participant("vic", playerProfileId: actor.Id),
            Participant("victor", playerProfileId: null));
        var handler = new GetSessionClaimablesQueryHandler(
            CurrentUser().Object, ProfileRepo(actor).Object, SessionRepo().Object, RsvpRepo().Object, repo.Object);

        var result = await handler.HandleAsync(new GetSessionClaimablesQuery(SessionId));

        result.AlreadyOnRoster.Should().BeTrue();
        result.Claimable.Should().BeEmpty();
    }

    [Fact]
    public async Task Unlinked_AsGameAdmin_ListsOnlyUnclaimedEntriesRegardlessOfOwnRoster()
    {
        var admin = new Mock<ICurrentUser>();
        admin.SetupGet(x => x.UserId).Returns(IdentityUserId);
        admin.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        var repo = GameRepo(
            Participant("victor", playerProfileId: null),
            Participant("linked", playerProfileId: Guid.NewGuid()),
            Participant("chidu", playerProfileId: null));
        var handler = new GetSessionUnlinkedParticipantsQueryHandler(
            admin.Object, SessionRepo().Object, repo.Object);

        var result = await handler.HandleAsync(new GetSessionUnlinkedParticipantsQuery(SessionId));

        result.Select(x => x.DisplayName).Should().BeEquivalentTo(["victor", "chidu"]);
    }

    [Fact]
    public async Task Unlinked_WhenCallerIsNotGameAdmin_IsForbidden()
    {
        var handler = new GetSessionUnlinkedParticipantsQueryHandler(
            CurrentUser().Object, SessionRepo().Object, GameRepo().Object);

        var act = () => handler.HandleAsync(new GetSessionUnlinkedParticipantsQuery(SessionId));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    private static PlayerProfile Profile(string name) =>
        new() { Id = Guid.NewGuid(), IdentityUserId = IdentityUserId, DisplayName = name };

    private static PickupPalGameParticipant Participant(string name, Guid? playerProfileId) =>
        new()
        {
            Id = Guid.NewGuid(),
            SessionId = SessionId,
            PickupPalParticipantId = name,
            DisplayName = name,
            PlayerProfileId = playerProfileId,
        };

    private static Mock<ICurrentUser> CurrentUser()
    {
        var u = new Mock<ICurrentUser>();
        u.SetupGet(x => x.UserId).Returns(IdentityUserId);
        return u;
    }

    private static Mock<IPlayerProfileRepository> ProfileRepo(PlayerProfile actor, params PlayerProfile[] linked)
    {
        var r = new Mock<IPlayerProfileRepository>();
        r.Setup(x => x.FindByIdentityUserIdAsync(IdentityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        var all = new[] { actor }.Concat(linked).ToArray();
        r.Setup(x => x.ListProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                all.Where(p => ids.Contains(p.Id)).ToArray());
        foreach (var profile in linked)
        {
            r.Setup(x => x.FindProfileAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        }

        return r;
    }

    private static PlayerProfile ClaimedProfile(string name) =>
        new() { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = name };

    private static PlayerProfile UnclaimedProfile(string name) =>
        new() { Id = Guid.NewGuid(), IdentityUserId = null, DisplayName = name };

    private static Mock<ISessionRepository> SessionRepo()
    {
        var r = new Mock<ISessionRepository>();
        r.Setup(x => x.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = SessionId, Title = "Bay Area Soccer" });
        return r;
    }

    private static Mock<IRsvpRepository> RsvpRepo()
    {
        var r = new Mock<IRsvpRepository>();
        r.Setup(x => x.ListGoingRosterAsync(SessionId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        r.Setup(x => x.ListActiveWaitlistRosterAsync(SessionId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return r;
    }

    private static Mock<IPickupPalGameRepository> GameRepo(params PickupPalGameParticipant[] participants)
    {
        var r = new Mock<IPickupPalGameRepository>();
        r.Setup(x => x.ListParticipantsAsync(SessionId, It.IsAny<CancellationToken>())).ReturnsAsync(participants);
        foreach (var p in participants)
        {
            r.Setup(x => x.FindParticipantAsync(p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        }

        return r;
    }

    private static ClaimParticipantCommandHandler ClaimHandler(
        PlayerProfile actor,
        Mock<IPickupPalGameRepository> repo,
        Mock<IStatsRepository>? statsRepo = null) =>
        new(
            CurrentUser().Object,
            Clock().Object,
            ProfileRepo(actor).Object,
            SessionRepo().Object,
            RsvpRepo().Object,
            repo.Object,
            (statsRepo ?? new Mock<IStatsRepository>()).Object,
            Mock.Of<IAuditLogRepository>(),
            Mock.Of<IUnitOfWork>());

    private static Mock<IClock> Clock()
    {
        var c = new Mock<IClock>();
        c.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 23, 4, 0, 0, DateTimeKind.Utc));
        return c;
    }
}
