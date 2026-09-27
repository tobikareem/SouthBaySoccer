namespace SouthBaySoccer.Services.Authentication;

public interface IExternalLauncher
{
    Task<bool> OpenPickupPalBotAsync(CancellationToken cancellationToken);
    Task<bool> OpenTermsAsync(CancellationToken cancellationToken);
    Task<bool> OpenPrivacyPolicyAsync(CancellationToken cancellationToken);

    /// <summary>Opens WhatsApp to the Pickup Pal bot with <paramref name="message"/> prefilled. Nothing is sent until the user taps.</summary>
    Task<bool> OpenWhatsAppMessageAsync(string message, CancellationToken cancellationToken);
}
