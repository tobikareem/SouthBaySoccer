using FluentAssertions;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.SeedData;

namespace SouthBaySoccer.Client.Tests;

public class SeedSessionAdminClientTests
{
    private static CreateSessionCommand ValidCommand(
        string venueName = "Cuesta Park",
        int capacity = 14,
        string format = "7v7") =>
        new(
            GameDateLocal: new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Unspecified),
            StartTimeLocal: new TimeSpan(18, 0, 0),
            CheckInOpenLocal: new TimeSpan(17, 50, 0),
            CheckInCloseLocal: new TimeSpan(18, 0, 0),
            VenueId: SeedFixtures.Venues[2].Id,
            VenueName: venueName,
            Format: format,
            Capacity: capacity,
            TeamCount: 2,
            RsvpDeadlineLocal: new TimeSpan(17, 0, 0));

    [Fact]
    public async Task GetDefaultsAsync_Seed_ReturnsManageableDefaultsAndSavedVenue()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var defaults = await client.GetDefaultsAsync(CancellationToken.None);

        defaults.CanManageSessions.Should().BeTrue();
        defaults.Formats.Should().Equal("5v5", "7v7", "9v9");
        defaults.TeamOptions.Should().Equal("2 teams", "3 teams", "4 teams");
        defaults.SavedVenue.Name.Should().Be("Marina Field");
        defaults.MinimumCapacity.Should().Be(1);
    }

    [Fact]
    public async Task SearchVenuesAsync_WithQuery_FiltersByNameOrLocality()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var results = await client.SearchVenuesAsync("palo alto", CancellationToken.None);

        results.Should().OnlyContain(venue => venue.Locality.Contains("Palo Alto"));
        results.Select(venue => venue.Name).Should().Contain("Stanford Turf");
    }

    [Fact]
    public async Task SearchVenuesAsync_EmptyQuery_ReturnsAllVenues()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var results = await client.SearchVenuesAsync("  ", CancellationToken.None);

        results.Should().HaveCount(SeedFixtures.Venues.Count);
    }

    [Fact]
    public async Task CreateVenueAsync_WithNewVenue_AddsSearchableSavedVenue()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var venue = await client.CreateVenueAsync("New Park", "Torrance", null, CancellationToken.None);
        var results = await client.SearchVenuesAsync("new", CancellationToken.None);

        venue.Name.Should().Be("New Park");
        venue.Locality.Should().Be("Torrance");
        venue.IsSaved.Should().BeTrue();
        results.Should().ContainSingle(item => item.Id == venue.Id);
    }

    [Fact]
    public async Task CreateDraftAsync_InvalidCapacity_ReturnsFailureWithoutPublishing()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var result = await client.CreateDraftAsync(ValidCommand(capacity: 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("capacity_invalid");
    }

    [Fact]
    public async Task CreateDraftAsync_InvalidCheckInWindow_ReturnsFailure()
    {
        var client = new SeedSessionAdminClient(new SeedState());
        var command = ValidCommand() with
        {
            CheckInOpenLocal = new TimeSpan(18, 10, 0),
            CheckInCloseLocal = new TimeSpan(18, 0, 0)
        };

        var result = await client.CreateDraftAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("checkin_window_invalid");
    }


    [Fact]
    public async Task CreateDraftAsync_CheckInCloseAfterStart_ReturnsFailure()
    {
        var client = new SeedSessionAdminClient(new SeedState());
        var command = ValidCommand() with
        {
            CheckInCloseLocal = new TimeSpan(18, 5, 0)
        };

        var result = await client.CreateDraftAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("checkin_close_after_start");
    }

    [Fact]
    public async Task Publish_AfterCreateDraft_AddsRsvpReadySessionToDashboardFeed()
    {
        var state = new SeedState();
        var adminClient = new SeedSessionAdminClient(state);
        var sessionsClient = new SeedSessionsClient(state);

        var draft = await adminClient.CreateDraftAsync(ValidCommand(), CancellationToken.None);
        var published = await adminClient.PublishAsync(draft.SessionId, CancellationToken.None);
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);
        var detail = await sessionsClient.GetSessionAsync(published.SessionId, CancellationToken.None);

        published.IsSuccess.Should().BeTrue();
        dashboard.ComingUpSessions.Should().ContainSingle(session => session.Id == published.SessionId);
        var feedCard = dashboard.ComingUpSessions.Single(session => session.Id == published.SessionId);
        feedCard.Title.Should().Be("Cuesta Park · 7v7");
        feedCard.Capacity.Should().Be(14);
        feedCard.GoingCount.Should().Be(0);
        detail!.IsRsvpAvailable.Should().BeTrue();
        detail.IsFull.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_SameDraftTwice_IsIdempotentAndDoesNotDuplicate()
    {
        var state = new SeedState();
        var adminClient = new SeedSessionAdminClient(state);
        var sessionsClient = new SeedSessionsClient(state);

        var draft = await adminClient.CreateDraftAsync(ValidCommand(), CancellationToken.None);
        var first = await adminClient.PublishAsync(draft.SessionId, CancellationToken.None);
        var second = await adminClient.PublishAsync(draft.SessionId, CancellationToken.None);
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);

        second.SessionId.Should().Be(first.SessionId);
        dashboard.ComingUpSessions
            .Count(session => session.Id == first.SessionId)
            .Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_UnknownDraft_ReturnsFailure()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var result = await client.PublishAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("draft_not_found");
    }

    [Fact]
    public async Task Reset_AfterPublish_RemovesPublishedSessionFromFeed()
    {
        var state = new SeedState();
        var adminClient = new SeedSessionAdminClient(state);
        var sessionsClient = new SeedSessionsClient(state);

        var draft = await adminClient.CreateDraftAsync(ValidCommand(), CancellationToken.None);
        var published = await adminClient.PublishAsync(draft.SessionId, CancellationToken.None);
        state.Reset();
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);

        dashboard.ComingUpSessions.Should().NotContain(session => session.Id == published.SessionId);
    }

    [Fact]
    public async Task AsyncMethods_CancelledToken_ThrowsOperationCanceledException()
    {
        var client = new SeedSessionAdminClient(new SeedState());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => client.GetDefaultsAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetDefaultsAsync_Seed_IncludesEditableRsvpDeadlineDefault()
    {
        var client = new SeedSessionAdminClient(new SeedState());

        var defaults = await client.GetDefaultsAsync(CancellationToken.None);

        defaults.DefaultRsvpDeadlineLocal.Should().NotBeNull();
        defaults.DefaultRsvpDeadlineLocal.Should().BeLessThanOrEqualTo(defaults.DefaultStartTimeLocal);
    }

    [Fact]
    public async Task CreateDraftAsync_MissingRsvpDeadline_ReturnsFailureWithoutPublishing()
    {
        var client = new SeedSessionAdminClient(new SeedState());
        var command = ValidCommand() with { RsvpDeadlineLocal = null };

        var result = await client.CreateDraftAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("rsvp_deadline_required");
    }

    [Fact]
    public async Task CreateDraftAsync_RsvpDeadlineAfterStart_ReturnsFailureWithoutPublishing()
    {
        var client = new SeedSessionAdminClient(new SeedState());
        var command = ValidCommand() with { RsvpDeadlineLocal = new TimeSpan(18, 1, 0) };

        var result = await client.CreateDraftAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("rsvp_deadline_after_start");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task CreateDraftAsync_InvalidTeamCount_ReturnsFailureWithoutPublishing(int teamCount)
    {
        var client = new SeedSessionAdminClient(new SeedState());
        var command = ValidCommand() with { TeamCount = teamCount };

        var result = await client.CreateDraftAsync(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("team_count_invalid");
    }

    [Fact]
    public async Task Publish_AfterCreateDraft_CreatesSessionDetailThatAcceptsGoingRsvp()
    {
        var state = new SeedState();
        var adminClient = new SeedSessionAdminClient(state);
        var sessionsClient = new SeedSessionsClient(state);
        var rosterClient = new SeedRosterClient(state);

        var draft = await adminClient.CreateDraftAsync(ValidCommand(), CancellationToken.None);
        var published = await adminClient.PublishAsync(draft.SessionId, CancellationToken.None);
        var rsvp = await rosterClient.SetRsvpIntentAsync(published.SessionId, true, CancellationToken.None);
        var detail = await sessionsClient.GetSessionAsync(published.SessionId, CancellationToken.None);
        var roster = await rosterClient.GetRosterAsync(published.SessionId, CancellationToken.None);

        rsvp.IsSuccess.Should().BeTrue();
        detail!.IsGoing.Should().BeTrue();
        detail.GoingCount.Should().Be(1);
        roster!.Going.Should().ContainSingle(entry => entry.Player.Id == SeedFixtures.CurrentPlayerId);
    }
}

