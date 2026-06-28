using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Maps;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateVenueCommandHandler(
    IValidator<CreateVenueCommand> validator,
    IVenueRepository venueRepository,
    IMapsService mapsService,
    IUnitOfWork unitOfWork)
{
    public async Task<VenueModel> HandleAsync(CreateVenueCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        string? mapsReference = null;
        if (!string.IsNullOrWhiteSpace(command.Address))
        {
            var coordinates = await mapsService.GeocodeAsync(command.Address.Trim(), cancellationToken);
            mapsReference = $"{coordinates.Latitude:0.######},{coordinates.Longitude:0.######}";
        }

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = command.Name.Trim(),
            Locality = command.Locality.Trim(),
            Address = string.IsNullOrWhiteSpace(command.Address) ? null : command.Address.Trim(),
            MapsProviderReference = mapsReference,
        };

        await venueRepository.AddAsync(venue, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(venue);
    }
}

public sealed class ListVenuesQueryHandler(IVenueRepository venueRepository)
{
    public async Task<IReadOnlyList<VenueModel>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        return venues.Select(SchedulingMappers.ToModel).ToArray();
    }
}
