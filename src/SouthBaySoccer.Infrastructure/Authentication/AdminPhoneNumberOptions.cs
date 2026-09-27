namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Options for phone numbers that should receive local administrative privileges. Both lists are
/// root Functions settings holding comma-separated phone numbers; the numbers themselves live only
/// in configuration, never in code or documentation.
/// </summary>
public sealed class AdminPhoneNumberOptions
{
    /// <summary>
    /// Gets or sets a comma-separated list of game-admin phone numbers.
    /// </summary>
    public string AdminPhoneNumbers { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a comma-separated list of super-admin (owner) phone numbers. A matching player
    /// is promoted to <c>PlayerRole.Owner</c>; a number in both lists is an owner.
    /// </summary>
    public string OwnerPhoneNumbers { get; set; } = string.Empty;
}
