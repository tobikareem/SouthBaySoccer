using FluentAssertions;
using SouthBaySoccer.SeedData;

namespace SouthBaySoccer.Client.Tests;

public class SeedFixtureSafetyTests
{
    [Fact]
    public void Players_InventedFixtureSet_ContainsNoContactOrPaymentIdentifiers()
    {
        var fixtureText = string.Join(
            "|",
            SeedFixtures.Players.SelectMany(
                player => new[]
                {
                    player.DisplayName,
                    player.Initials,
                    player.Position
                }));

        fixtureText.Should().NotContain("@");
        fixtureText.ToLowerInvariant().Should().NotContain("stripe");
        fixtureText.ToLowerInvariant().Should().NotContain("payment");
        fixtureText.Should().NotMatchRegex(@"\+?\d[\d\s().-]{7,}\d");
        SeedFixtures.Players.Select(player => player.Id).Should().OnlyHaveUniqueItems();
    }
}
