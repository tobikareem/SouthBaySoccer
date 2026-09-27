using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Features.Groups;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Groups;

public sealed class GroupNameVisibilityTests
{
    [Theory]
    [InlineData(" training, ARCHIVE ,,training ", "Saturday Training", false)]
    [InlineData(" training, ARCHIVE ,,training ", "archive soccer", false)]
    [InlineData(" training, ARCHIVE ,,training ", "Test soccer", true)]
    [InlineData("", "test tmp 120363", true)]
    [InlineData(" , , ", "South Bay Soccer", true)]
    public void IsVisible_WhenConfigured_UsesTrimmedNonemptyPatterns(string csv, string name, bool expected)
    {
        var visibility = new GroupNameVisibility(csv);

        var result = visibility.IsVisible(name);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetAvailableGroups_WhenNamesAreExcluded_PreservesNamedNumericIdsOnly()
    {
        var client = new Mock<IPickupPalGroupClient>();
        client.Setup(x => x.GetAllGroupsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PickupPalGroupChat("1", "Saturday TeSt group", null, "active", 1, null),
            new PickupPalGroupChat("2", "TMP Sunday", null, "active", 1, null),
            new PickupPalGroupChat("3", "Group 120363123", null, "active", 1, null),
            new PickupPalGroupChat("120363456@g.us", "South Bay Soccer", null, "active", 1, null),
            new PickupPalGroupChat("120363789@g.us", " ", null, "active", 1, null),
        ]);
        var handler = new GetAvailableGroupsQueryHandler(client.Object, new GroupNameVisibility());

        var result = await handler.HandleAsync(new GetAvailableGroupsQuery());

        result.Should().ContainSingle().Which.ExternalId.Should().Be("120363456@g.us");
    }
}
