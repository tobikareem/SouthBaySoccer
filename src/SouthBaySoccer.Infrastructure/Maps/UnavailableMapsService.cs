using SouthBaySoccer.Application.Abstractions.Maps;

namespace SouthBaySoccer.Infrastructure.Maps;

internal sealed class UnavailableMapsService : IMapsService
{
    public Task<(double Latitude, double Longitude)> GeocodeAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Maps geocoding provider is not configured.");
    }
}
