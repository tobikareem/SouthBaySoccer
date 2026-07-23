using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Stats;

public sealed class StatsCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenFeedbackContainsSelfVote_ThrowsConflict()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Ada" };
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.FindMatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SouthBaySoccer.Domain.Entities.Stats.Match { Id = Guid.NewGuid() });
        var handler = new SubmitPeerFeedbackCommandHandler(
            currentUser.Object,
            new SubmitPeerFeedbackCommandValidator(),
            profiles.Object,
            stats.Object,
            Mock.Of<IUnitOfWork>());

        var act = () => handler.HandleAsync(new SubmitPeerFeedbackCommand(
            Guid.NewGuid(),
            [new PlayerRatingInput(profile.Id, 10)],
            [],
            null));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        stats.Verify(x => x.SubmitPeerFeedbackAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<PlayerRatingVote>>(),
            It.IsAny<IReadOnlyList<PlayerLike>>(),
            It.IsAny<MatchAward?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task HandleAsync_WhenCaptainReviewConflictsWithExistingDecision_MarksMatchNeedsReviewAndAuditsCorrection()
    {
        var identityUserId = Guid.NewGuid();
        var captain = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Ada" };
        var match = new SouthBaySoccer.Domain.Entities.Stats.Match { Id = Guid.NewGuid(), Status = MatchStatus.Completed };
        var matchEvent = new MatchEvent
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            PlayerProfileId = Guid.NewGuid(),
            EventType = MatchEventType.Goal,
            ReviewStatus = MatchEventReviewStatus.Approved,
        };
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        currentUser.Setup(x => x.HasPolicy("CanRecordStats")).Returns(false);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 7, 22, 0, 0, DateTimeKind.Utc));
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(captain);
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.FindMatchEventAsync(matchEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(matchEvent);
        stats.Setup(x => x.FindMatchAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        stats.Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchTeam { Id = Guid.NewGuid(), MatchId = match.Id, CaptainPlayerProfileId = captain.Id }]);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ReviewMatchEventCommandHandler(
            currentUser.Object,
            clock.Object,
            new ReviewMatchEventCommandValidator(),
            profiles.Object,
            stats.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(new ReviewMatchEventCommand(match.Id, matchEvent.Id, false, "wrong scorer"));

        result.MatchId.Should().Be(match.Id);
        match.Status.Should().Be(MatchStatus.NeedsReview);
        stats.Verify(x => x.AddStatCorrectionAsync(
            It.Is<StatCorrection>(correction =>
                correction.MatchId == match.Id
                && correction.CorrectedByPlayerProfileId == captain.Id
                && correction.Reason == "wrong scorer"
                && correction.CorrectedAtUtc == new DateTime(2026, 7, 7, 22, 0, 0, DateTimeKind.Utc)),
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenProfileStatsReassigned_WritesDedicatedAuditRecord()
    {
        var sourceProfileId = Guid.NewGuid();
        var targetProfileId = Guid.NewGuid();
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc));
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.ReassignProfileStatsAsync(sourceProfileId, targetProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ReassignProfileStatsCommandHandler(
            clock.Object,
            new ReassignProfileStatsCommandValidator(),
            stats.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(new ReassignProfileStatsCommand(sourceProfileId, targetProfileId));

        result.AffectedCount.Should().Be(7);
        stats.Verify(x => x.AddProfileStatReassignmentAuditAsync(
            It.Is<ProfileStatReassignmentAudit>(audit =>
                audit.SourceGuestPlayerProfileId == sourceProfileId
                && audit.TargetPlayerProfileId == targetProfileId
                && audit.AffectedCount == 7
                && audit.ReassignedAtUtc == new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc)),
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task HandleAsync_WhenReviewRouteMatchDoesNotOwnEvent_ThrowsNotFoundBeforeMutation()
    {
        var identityUserId = Guid.NewGuid();
        var captain = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Ada" };
        var eventMatchId = Guid.NewGuid();
        var routeMatchId = Guid.NewGuid();
        var matchEvent = new MatchEvent
        {
            Id = Guid.NewGuid(),
            MatchId = eventMatchId,
            EventType = MatchEventType.Goal,
            ReviewStatus = MatchEventReviewStatus.Pending,
        };
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        var clock = new Mock<IClock>();
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(captain);
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.FindMatchEventAsync(matchEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(matchEvent);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ReviewMatchEventCommandHandler(
            currentUser.Object,
            clock.Object,
            new ReviewMatchEventCommandValidator(),
            profiles.Object,
            stats.Object,
            unitOfWork.Object);

        var act = () => handler.HandleAsync(new ReviewMatchEventCommand(routeMatchId, matchEvent.Id, true, null));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
        matchEvent.ReviewStatus.Should().Be(MatchEventReviewStatus.Pending);
        stats.Verify(x => x.FindMatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        stats.Verify(x => x.AddStatCorrectionAsync(It.IsAny<StatCorrection>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task HandleAsync_WhenMatchResultsConflictWithExistingResults_MarksMatchNeedsReviewAndAuditsCorrection()
    {
        var identityUserId = Guid.NewGuid();
        var actor = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Ada" };
        var match = new SouthBaySoccer.Domain.Entities.Stats.Match { Id = Guid.NewGuid(), Status = MatchStatus.Completed };
        var teamId = Guid.NewGuid();
        var opponentTeamId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        currentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 9, 1, 0, 0, DateTimeKind.Utc));
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.FindMatchAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        stats.Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchTeam { Id = teamId, MatchId = match.Id }, new MatchTeam { Id = opponentTeamId, MatchId = match.Id }]);
        stats.Setup(x => x.ListMatchResultsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchResult { MatchId = match.Id, MatchTeamId = teamId, Wins = 1, Draws = 0, Losses = 0, GoalsFor = 3, GoalsAgainst = 1 }, new MatchResult { MatchId = match.Id, MatchTeamId = opponentTeamId, Wins = 0, Draws = 0, Losses = 1, GoalsFor = 1, GoalsAgainst = 3 }]);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new RecordMatchResultsCommandHandler(
            currentUser.Object,
            clock.Object,
            new RecordMatchResultsCommandValidator(),
            profiles.Object,
            stats.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(new RecordMatchResultsCommand(
            match.Id,
            [new MatchResultInput(teamId, Wins: 0, Draws: 1, Losses: 0, GoalsFor: 2, GoalsAgainst: 2), new MatchResultInput(opponentTeamId, Wins: 0, Draws: 1, Losses: 0, GoalsFor: 2, GoalsAgainst: 2)]));

        result.AffectedCount.Should().Be(1);
        match.Status.Should().Be(MatchStatus.NeedsReview);
        stats.Verify(x => x.UpsertMatchResultsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<MatchResult>>(), It.IsAny<CancellationToken>()), Times.Never);
        stats.Verify(x => x.AddStatCorrectionAsync(
            It.Is<StatCorrection>(correction =>
                correction.MatchId == match.Id
                && correction.CorrectedByPlayerProfileId == actor.Id
                && correction.Reason == "Conflicting match result submission."
                && correction.CorrectedAtUtc == new DateTime(2026, 7, 9, 1, 0, 0, DateTimeKind.Utc)),
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}



public sealed class StatsQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenLeaderboardRowsReturned_AppliesRankOffsetAndMetricNote()
    {
        var seasonId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var seasons = new Mock<ISeasonRepository>();
        seasons.Setup(x => x.GetByIdAsync(seasonId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SouthBaySoccer.Domain.Entities.Scheduling.Season { Id = seasonId, Name = "Season 2026" });
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.ListSeasonLeaderboardAsync(seasonId, StatLeaderboardMetric.Goals, 25, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new LeaderboardReadModel(playerId, "Ada Okafor", "Forward", false, Guid.NewGuid(), 8, 10, 4, 8.2m, 6, 12, 2, 10m),
            ]);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc));
        var handler = new GetSeasonLeaderboardQueryHandler(
            new GetSeasonLeaderboardQueryValidator(),
            seasons.Object,
            stats.Object,
            clock.Object);

        var result = await handler.HandleAsync(new GetSeasonLeaderboardQuery(seasonId, StatLeaderboardMetric.Goals, Page: 2, PageSize: 25));

        result.SeasonLabel.Should().Be("Season 2026");
        result.Rows.Single().Rank.Should().Be(26);
        result.Rows.Single().Player.Initials.Should().Be("AO");
        result.Rows.Single().Value.Should().Be(10m);
        result.Note.Should().Contain("approved goals");
    }

    [Fact]
    public async Task HandleAsync_WhenSeasonIdNull_ResolvesCurrentActiveSeason()
    {
        var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
        var activeSeasonId = Guid.NewGuid();
        var seasons = new Mock<ISeasonRepository>();
        seasons.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SouthBaySoccer.Domain.Entities.Scheduling.Season
            {
                Id = activeSeasonId,
                Name = "Season 2026",
                StartsAtUtc = now.AddMonths(-1),
                EndsAtUtc = now.AddMonths(1),
            }]);
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.ListSeasonLeaderboardAsync(activeSeasonId, StatLeaderboardMetric.Goals, 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new LeaderboardReadModel(Guid.NewGuid(), "Ada Okafor", "Forward", false, Guid.NewGuid(), 8, 10, 4, 8.2m, 6, 12, 2, 10m),
            ]);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        var handler = new GetSeasonLeaderboardQueryHandler(
            new GetSeasonLeaderboardQueryValidator(),
            seasons.Object,
            stats.Object,
            clock.Object);

        var result = await handler.HandleAsync(new GetSeasonLeaderboardQuery(null, StatLeaderboardMetric.Goals, Page: 1, PageSize: 25));

        result.SeasonId.Should().Be(activeSeasonId);
        result.SeasonLabel.Should().Be("Season 2026");
        result.Rows.Should().ContainSingle();
        seasons.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSeasonIdNullAndNoSeasonCoversToday_ReturnsEmptyLeaderboard()
    {
        var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
        var seasons = new Mock<ISeasonRepository>();
        seasons.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SouthBaySoccer.Domain.Entities.Scheduling.Season
            {
                Id = Guid.NewGuid(),
                Name = "Next Season",
                StartsAtUtc = now.AddMonths(1),
                EndsAtUtc = now.AddMonths(4),
            }]);
        var stats = new Mock<IStatsRepository>();
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        var handler = new GetSeasonLeaderboardQueryHandler(
            new GetSeasonLeaderboardQueryValidator(),
            seasons.Object,
            stats.Object,
            clock.Object);

        var result = await handler.HandleAsync(new GetSeasonLeaderboardQuery(null, StatLeaderboardMetric.Goals, Page: 1, PageSize: 25));

        result.SeasonId.Should().Be(Guid.Empty);
        result.SeasonLabel.Should().Be("No active season");
        result.Rows.Should().BeEmpty();
        result.Note.Should().Contain("approved goals");
        stats.Verify(
            x => x.ListSeasonLeaderboardAsync(It.IsAny<Guid>(), It.IsAny<StatLeaderboardMetric>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenRecentFormCounterExceedsTeamCount_ThrowsConflict()
    {
        var playerId = Guid.NewGuid();
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.ListPlayerRecentFormAsync(playerId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlayerRecentFormReadModel(Guid.NewGuid(), new DateTime(2026, 7, 7, 22, 0, 0, DateTimeKind.Utc), TeamCount: 2, Wins: 2, Draws: 0, Losses: 0),
            ]);
        var handler = new GetPlayerRecentFormQueryHandler(new GetPlayerRecentFormQueryValidator(), stats.Object);

        var act = () => handler.HandleAsync(new GetPlayerRecentFormQuery(playerId, 5));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    [Fact]
    public async Task HandleAsync_WhenMyStatsRequested_ResolvesCurrentPlayerProfile()
    {
        var identityUserId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerProfile { Id = playerId, IdentityUserId = identityUserId, DisplayName = "Ada" });
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.GetPlayerStatsAsync(playerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerStatSummaryReadModel(playerId, "Ada", "Forward", false, identityUserId, 12, 7, 5, 8.1m, 10, 9, 2));
        var getPlayerStatsHandler = new GetPlayerStatsQueryHandler(new GetPlayerStatsQueryValidator(), stats.Object);
        var handler = new GetMyPlayerStatsQueryHandler(currentUser.Object, profiles.Object, getPlayerStatsHandler);

        var result = await handler.HandleAsync(new GetMyPlayerStatsQuery(SeasonId: null));

        result.PlayerProfileId.Should().Be(playerId);
        result.Matches.Should().Be(12);
        result.Goals.Should().Be(7);
    }
}
