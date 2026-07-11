using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class VenueHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenQueryIsNullOrWhitespace_ReturnsAllActiveVenues()
    {
        var handler = new ListVenuesQueryHandler(VenueRepository());

        var result = await handler.HandleAsync(query: "   ");

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task HandleAsync_WhenQueryMatchesName_FiltersCaseInsensitively()
    {
        var handler = new ListVenuesQueryHandler(VenueRepository());

        var result = await handler.HandleAsync(query: "marina");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Marina Field");
    }

    [Fact]
    public async Task HandleAsync_WhenQueryMatchesLocality_FiltersToThatLocality()
    {
        var handler = new ListVenuesQueryHandler(VenueRepository());

        var result = await handler.HandleAsync(query: "Torrance");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Wilson Park");
    }

    [Fact]
    public async Task HandleAsync_WhenQueryMatchesNothing_ReturnsEmpty()
    {
        var handler = new ListVenuesQueryHandler(VenueRepository());

        var result = await handler.HandleAsync(query: "no-such-venue");

        result.Should().BeEmpty();
    }

    private static IVenueRepository VenueRepository()
    {
        var repository = new Mock<IVenueRepository>();
        repository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Venue { Id = Guid.NewGuid(), Name = "Marina Field", Locality = "Redondo Beach" },
                new Venue { Id = Guid.NewGuid(), Name = "Wilson Park", Locality = "Torrance" },
                new Venue { Id = Guid.NewGuid(), Name = "Stanford Turf", Locality = "Palo Alto" },
            ]);
        return repository.Object;
    }
}
