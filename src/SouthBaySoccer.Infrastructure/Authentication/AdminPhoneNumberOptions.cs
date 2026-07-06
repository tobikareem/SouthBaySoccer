namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Options for phone numbers that should receive local game-admin privileges.
/// </summary>
public sealed class AdminPhoneNumberOptions
{
    /// <summary>
    /// Gets or sets a comma-separated list of admin phone numbers.
    /// </summary>
    public string AdminPhoneNumbers { get; set; } = string.Empty;
}
