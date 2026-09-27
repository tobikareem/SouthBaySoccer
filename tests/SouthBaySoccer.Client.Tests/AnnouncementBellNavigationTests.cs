using System.Text.Json;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public sealed class AnnouncementBellNavigationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task OpenAnnouncements_WhenBadgeHasTarget_OpensUnreadGroupOtherwisePrimary(int unread)
    {
        var state = new SeedState();
        var groups = new SeedGroupsClient();
        var target = unread > 0 ? SeedGroupsClient.MorningId : (Guid?)null;
        var client = new Mock<IAnnouncementsClient>();
        client.Setup(x => x.GetUnreadCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnreadAnnouncementsResponse(unread, target));
        var navigation = new Mock<IAnnouncementsNavigator>();
        var model = new SessionsHomePageModel(new SeedSessionsClient(state), Mock.Of<ISessionsNavigator>(),
            new SeedProfileClient(), groups, Mock.Of<IDismissedStatsPromptStore>(),
            new ClientResponseCache(TimeProvider.System), TimeProvider.System, client.Object, navigation.Object);

        await model.AppearingCommand.ExecuteAsync(null);
        await model.OpenAnnouncementsCommand.ExecuteAsync(null);

        navigation.Verify(x => x.GoToAnnouncementsAsync(target ?? SeedGroupsClient.BayAreaId), Times.Once);
        navigation.VerifyNoOtherCalls();
    }

    [Fact]
    public void Deserialize_WhenLegacyResponseOmitsTarget_PreservesCountAndDefaultsTargetToNull()
    {
        var response = JsonSerializer.Deserialize<UnreadAnnouncementsResponse>("{\"unreadCount\":4}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        response.Should().Be(new UnreadAnnouncementsResponse(4));
    }
}
