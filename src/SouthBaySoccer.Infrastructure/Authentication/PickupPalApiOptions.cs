namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Options for the Pickup Pal API integration.
/// </summary>
public sealed class PickupPalApiOptions
{
    /// <summary>Gets or sets the Pickup Pal API base URL.</summary>
    public string BaseUrl { get; set; } = "https://pickuppal-bot-dev.up.railway.app";
}
