using SouthBaySoccer.Configuration;

namespace SouthBaySoccer.Services.Authentication;

public sealed class ExternalLauncher(PickupPalOptions options) : IExternalLauncher
{
    public Task<bool> OpenPickupPalBotAsync(CancellationToken cancellationToken) =>
        OpenAsync(options.BotUri, cancellationToken);

    public Task<bool> OpenTermsAsync(CancellationToken cancellationToken) =>
        OpenAsync(options.TermsUri, cancellationToken);

    public Task<bool> OpenPrivacyPolicyAsync(CancellationToken cancellationToken) =>
        OpenAsync(options.PrivacyPolicyUri, cancellationToken);

    public Task<bool> OpenWhatsAppMessageAsync(string message, CancellationToken cancellationToken) =>
        OpenAsync(options.CreateWhatsAppMessageUri(message), cancellationToken);

    private static async Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await Launcher.Default.CanOpenAsync(uri))
        {
            return false;
        }

        return await Launcher.Default.OpenAsync(uri);
    }
}
