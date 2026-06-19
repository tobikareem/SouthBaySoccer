using FluentAssertions;
using SouthBaySoccer.Configuration;

namespace SouthBaySoccer.Client.Tests;

public class ClientDataSourceOptionsTests
{
    [Theory]
    [InlineData(null, ClientDataSource.Seed)]
    [InlineData("", ClientDataSource.Seed)]
    [InlineData("Seed", ClientDataSource.Seed)]
    [InlineData("api", ClientDataSource.Api)]
    public void FromValue_ConfiguredValue_ReturnsTypedSelection(
        string? configuredValue,
        ClientDataSource expected)
    {
        var options = ClientDataSourceOptions.FromValue(configuredValue);

        options.DataSource.Should().Be(expected);
    }

    [Fact]
    public void FromValue_UnknownValue_FailsFast()
    {
        var act = () => ClientDataSourceOptions.FromValue("LocalDatabase");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LocalDatabase*Seed or Api*");
    }
}
