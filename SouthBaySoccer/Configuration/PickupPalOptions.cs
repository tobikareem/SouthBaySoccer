namespace SouthBaySoccer.Configuration;

public sealed class PickupPalOptions
{
    public const string DefaultApiBaseUrl = "http://localhost:7071/api/";
    public const string DefaultBotDisplayNumber = "+1 (650) 220-5416";
    public const string DefaultBotUri = "https://wa.me/16502205416";
    public const string DefaultSignupUri = "https://pickuppal.app/";
    public const string DefaultCallbackUri = "southbaysoccer://auth/whatsapp";

    public Uri ApiBaseUri { get; init; } = new(DefaultApiBaseUrl);
    public string BotDisplayNumber { get; init; } = DefaultBotDisplayNumber;
    public Uri BotUri { get; init; } = new(DefaultBotUri);
    public Uri SignupUri { get; init; } = new(DefaultSignupUri);
    public Uri CallbackUri { get; init; } = new(DefaultCallbackUri);
}

