using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Identity;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>Repository for local-first player registrations.</summary>
public interface IPlayerRegistrationRepository : IRepository<PlayerRegistration>
{
    /// <summary>
    /// Finds the registration for a phone hash that is still waiting on Pickup Pal, so a retry
    /// updates the same row instead of creating a duplicate.
    /// </summary>
    /// <param name="phoneNumberHash">The SHA-256 hash of the E.164 phone number.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The awaiting registration, or <see langword="null"/> when none exists.</returns>
    Task<PlayerRegistration?> FindAwaitingExternalByPhoneNumberHashAsync(
        string phoneNumberHash,
        CancellationToken cancellationToken = default);
}
