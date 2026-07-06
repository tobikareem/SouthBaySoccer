namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Thrown when Pickup Pal has no user for a submitted sign-in phone number.
/// </summary>
public sealed class PickupPalUserNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PickupPalUserNotFoundException"/> class.</summary>
    public PickupPalUserNotFoundException()
        : base("Pickup Pal user was not found.")
    {
    }
}
