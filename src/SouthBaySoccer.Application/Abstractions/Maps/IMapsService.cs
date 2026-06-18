namespace SouthBaySoccer.Application.Abstractions.Maps;

/// <summary>
/// Application port for geocoding and mapping operations (Azure Maps in Infrastructure).
/// </summary>
public interface IMapsService
{
    /// <summary>
    /// Resolves a free-form address into geographic coordinates.
    /// </summary>
    /// <param name="address">The free-form address to geocode.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The resolved latitude and longitude.</returns>
    Task<(double Latitude, double Longitude)> GeocodeAsync(string address, CancellationToken cancellationToken = default);
}
