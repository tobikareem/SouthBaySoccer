using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Operations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>Repository for pending phone sign-ins awaiting WhatsApp possession proof.</summary>
public interface IPendingPhoneSignInRepository
{
    /// <summary>Adds a new pending sign-in.</summary>
    /// <param name="pendingSignIn">The pending sign-in to add.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task AddAsync(PendingPhoneSignIn pendingSignIn, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing pending sign-in as modified.</summary>
    /// <param name="pendingSignIn">The pending sign-in to update.</param>
    void Update(PendingPhoneSignIn pendingSignIn);

    /// <summary>
    /// Lists every pending sign-in for a Pickup Pal user that is unconsumed and unexpired at
    /// <paramref name="nowUtc"/>, newest first. Completion consumes all of them so a login token
    /// cannot be replayed against an older row.
    /// </summary>
    /// <param name="pickupPalUserId">The Pickup Pal user id the login token resolved to.</param>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The active pending sign-ins, empty when none exist.</returns>
    Task<IReadOnlyList<PendingPhoneSignIn>> ListActiveByPickupPalUserIdAsync(
        string pickupPalUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
