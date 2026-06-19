using FluentAssertions;
using SouthBaySoccer.SeedData;

namespace SouthBaySoccer.Client.Tests;

public class SeedSessionsAndRosterClientTests
{
    [Fact]
    public async Task GetDashboardAsync_FreshStates_ReturnsDeterministicWireframeFixtures()
    {
        var firstClient = new SeedSessionsClient(new SeedState());
        var secondClient = new SeedSessionsClient(new SeedState());

        var first = await firstClient.GetDashboardAsync(CancellationToken.None);
        var second = await secondClient.GetDashboardAsync(CancellationToken.None);

        first.Should().BeEquivalentTo(second);
        first.FeaturedSession.Title.Should().Be("Marina Field · Saturday pickup");
        first.FeaturedSession.GoingCount.Should().Be(16);
        first.FeaturedSession.Capacity.Should().Be(20);
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
        guest.IdentityId.Should().BeNull();
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
