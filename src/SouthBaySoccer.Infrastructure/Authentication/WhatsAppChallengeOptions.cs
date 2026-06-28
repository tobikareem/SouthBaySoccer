namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Options for local WhatsApp sign-in challenge persistence.
/// </summary>
public sealed class WhatsAppChallengeOptions
{
    /// <summary>Gets or sets the challenge lifetime.</summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(10);
}
