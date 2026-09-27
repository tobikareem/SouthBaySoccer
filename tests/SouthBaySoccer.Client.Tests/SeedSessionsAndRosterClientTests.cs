using FluentAssertions;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;

namespace SouthBaySoccer.Client.Tests;

public class SeedSessionsAndRosterClientTests
{
    [Fact]
    public async Task GetSession_FullWaitlistedSession_AllowsWithdrawalAndUpdatesDetail()
    {
        var state = new SeedState();
        state.JoinWaitlist(SeedFixtures.StanfordSessionId).IsSuccess.Should().BeTrue();
        var pageModel = DetailPage(state, SeedFixtures.StanfordSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);
        var detail = state.GetSession(SeedFixtures.StanfordSessionId);
        detail.Should().NotBeNull();
        detail?.IsFull.Should().BeTrue();
        detail?.IsWaitlisted.Should().BeTrue();
        detail?.IsRsvpAvailable.Should().BeTrue();
        pageModel.CanRsvp.Should().BeTrue();

        await pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        state.GetSession(SeedFixtures.StanfordSessionId)?.IsWaitlisted.Should().BeFalse();
        state.GetRoster(SeedFixtures.StanfordSessionId)?.Waitlist
            .Should().NotContain(entry => entry.Player.Id == SeedFixtures.CurrentPlayerId);
        pageModel.CanRsvp.Should().BeFalse("the full game has no held spot left to withdraw");
    }

    [Fact]
    public async Task GetSession_FullGoingSession_AllowsWithdrawal()
    {
        var state = new SeedState();
        var editable = state.GetSessionForEdit(SeedFixtures.MarinaSessionId);
        editable.Should().NotBeNull();
        state.UpdateSession(SeedFixtures.MarinaSessionId, editable!.Command with { Capacity = 16 })
            .IsSuccess.Should().BeTrue(); // The seed fixture always has a managed Marina session.
        var pageModel = DetailPage(state, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);
        state.GetSession(SeedFixtures.MarinaSessionId)?.IsFull.Should().BeTrue();
        pageModel.CanRsvp.Should().BeTrue();

        await pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        state.GetSession(SeedFixtures.MarinaSessionId)?.IsGoing.Should().BeFalse();
        state.GetRoster(SeedFixtures.MarinaSessionId)?.Going.Should().HaveCount(15);
    }

    [Fact]
    public async Task GetSession_FullSessionWithoutHeldSpot_DoesNotAllowPrimaryRsvp()
    {
        var state = new SeedState();
        var pageModel = DetailPage(state, SeedFixtures.StanfordSessionId);

        await pageModel.LoadCommand.ExecuteAsync(null);
        await pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        pageModel.CanRsvp.Should().BeFalse();
        state.GetSession(SeedFixtures.StanfordSessionId)?.IsGoing.Should().BeFalse();
        state.GetSession(SeedFixtures.StanfordSessionId)?.IsWaitlisted.Should().BeFalse();
    }

    [Fact]
    public void GetSession_CanceledSession_ClosesRsvpWindow()
    {
        var state = new SeedState();

        state.CancelSession(SeedFixtures.MarinaSessionId);

        state.GetSession(SeedFixtures.MarinaSessionId)?.IsRsvpAvailable.Should().BeFalse();
    }

    private static SessionDetailPageModel DetailPage(SeedState state, Guid sessionId)
    {
        var pageModel = new SessionDetailPageModel(new SeedSessionsClient(state), new SeedRosterClient(state));
        pageModel.ApplyQueryAttributes(new Dictionary<string, object> { ["sessionId"] = sessionId.ToString("D") });
        return pageModel;
    }

    [Fact]
    public async Task GetDashboardAsync_FreshStates_ReturnsDeterministicWireframeFixtures()
    {
        var firstClient = new SeedSessionsClient(new SeedState());
        var secondClient = new SeedSessionsClient(new SeedState());

        var first = await firstClient.GetDashboardAsync(CancellationToken.None);
        var second = await secondClient.GetDashboardAsync(CancellationToken.None);

        first.Should().BeEquivalentTo(second);
        var featured = first.FeaturedSession;
        featured.Should().NotBeNull();
        featured!.Title.Should().Be("Marina Field · Saturday pickup");
        featured.GoingCount.Should().Be(16);
        featured.Capacity.Should().Be(20);
        first.ComingUpSessions.Single().Title.Should().Be("Stanford Turf · 5v5");
        first.ComingUpSessions.Single().WaitlistCount.Should().Be(3);
    }

    [Fact]
    public async Task SetRsvpIntentAsync_ApplicationStateChangesAndReset_RestoresImmutableBaseline()
    {
        var state = new SeedState();
        var rosterClient = new SeedRosterClient(state);
        var baselineFixture = SeedFixtures.Rosters[SeedFixtures.MarinaSessionId];

        var result = await rosterClient.SetRsvpIntentAsync(
            SeedFixtures.MarinaSessionId,
            false,
            CancellationToken.None);
        var changed = await rosterClient.GetRosterAsync(
            SeedFixtures.MarinaSessionId,
            CancellationToken.None);
        state.Reset();
        var reset = await rosterClient.GetRosterAsync(
            SeedFixtures.MarinaSessionId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        changed!.Going.Should().HaveCount(15);
        reset!.Going.Should().HaveCount(16);
        baselineFixture.Going.Should().HaveCount(16);
        baselineFixture.Going.Should().Contain(entry => entry.IsCurrentPlayer);
    }

    [Fact]
    public async Task GetRosterAsync_StanfordRoster_ContainsOrderedGuestWaitlistEntry()
    {
        var client = new SeedRosterClient(new SeedState());

        var roster = await client.GetRosterAsync(
            SeedFixtures.StanfordSessionId,
            CancellationToken.None);

        roster!.Going.Should().HaveCount(20);
        roster.Waitlist.Select(entry => entry.Position).Should().Equal(1, 2, 3);
        var guest = roster.Waitlist[0].Player;
        guest.DisplayName.Should().Be("Tunde B.");
        guest.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task JoinWaitlistAsync_FullSession_AddsCurrentPlayerToApplicationState()
    {
        var state = new SeedState();
        var sessionsClient = new SeedSessionsClient(state);
        var rosterClient = new SeedRosterClient(state);

        var result = await sessionsClient.JoinWaitlistAsync(
            SeedFixtures.StanfordSessionId,
            CancellationToken.None);
        var roster = await rosterClient.GetRosterAsync(
            SeedFixtures.StanfordSessionId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        roster!.Waitlist.Should().HaveCount(4);
        roster.Waitlist[^1].Player.Id.Should().Be(SeedFixtures.CurrentPlayerId);
        roster.Waitlist[^1].Position.Should().Be(4);
    }

    [Fact]
    public async Task AsyncMethods_CancelledToken_ThrowsOperationCanceledException()
    {
        var client = new SeedSessionsClient(new SeedState());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => client.GetDashboardAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
