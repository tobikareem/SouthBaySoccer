using Microsoft.Extensions.Configuration;

namespace SouthBaySoccer.Configuration;

public sealed class PickupPalOptions
{
    public const string DefaultApiBaseUrl = "http://localhost:7071/api/";
    public const string AndroidDebugApiBaseUrl = "http://10.0.2.2:7071/api/";
    public const string ProductionApiBaseUrl =
        "https://southbaysoccerfunc-cndha8gtc4bxdtfe.westus2-01.azurewebsites.net/api/";
    public const string ApiBaseUrlKey = "ApiBaseUrl";
    public const string ProductionApiBaseUrlKey = "PrdApiBaseUrl";
    public const string DefaultBotDisplayNumber = "+1 (650) 220-5416";
    public const string DefaultBotUri = "https://www.pickuppal.xyz/bot-setup";
    /// <summary>Pickup Pal terms of use. Path is unconfirmed with the Pickup Pal owner; override via PickupPal:TermsUri.</summary>
    public const string DefaultTermsUri = "https://www.pickuppal.xyz/terms";
    public const string DefaultPrivacyPolicyUri = "https://tobikareem.github.io/SouthBaySoccer/privacy.html";
    public const string DefaultCallbackUri = "southbaysoccer://auth/whatsapp";
    /// <summary>Host the Pickup Pal bot links back to for register/login (universal / app links, M13.1).</summary>
    public const string DefaultAppLinkBaseUri = "https://n9jabay.desolatravels.com/";

    public Uri ApiBaseUri { get; init; } = new(DefaultApiBaseUrl);
    public string BotDisplayNumber { get; init; } = DefaultBotDisplayNumber;
    public Uri BotUri { get; init; } = new(DefaultBotUri);
    public Uri TermsUri { get; init; } = new(DefaultTermsUri);
    public Uri PrivacyPolicyUri { get; init; } = new(DefaultPrivacyPolicyUri);
    public Uri CallbackUri { get; init; } = new(DefaultCallbackUri);
    public Uri AppLinkBaseUri { get; init; } = new(DefaultAppLinkBaseUri);

    /// <summary>Digits-only bot number for wa.me links, derived from the display number.</summary>
    public string BotWhatsAppDigits => new(BotDisplayNumber.Where(char.IsDigit).ToArray());

    /// <summary>Builds the wa.me deep link that opens WhatsApp with <paramref name="message"/> prefilled.</summary>
    public Uri CreateWhatsAppMessageUri(string message) =>
        new($"https://wa.me/{BotWhatsAppDigits}?text={Uri.EscapeDataString(message)}");

    public static PickupPalOptions FromConfiguration(
        IConfiguration configuration,
        string defaultApiBaseUrl = DefaultApiBaseUrl
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredApiBaseUrl =
            FirstNonBlank(
                configuration[ProductionApiBaseUrlKey],
                configuration[ApiBaseUrlKey],
                configuration[$"PickupPal:{ApiBaseUrlKey}"],
                configuration["PickupPal:ApiBaseUri"]
            ) ?? defaultApiBaseUrl;

        // log the configuredApiBaseUrl for debugging purposes
        Console.WriteLine($"... Configured API Base URL: {configuredApiBaseUrl}");

        return new PickupPalOptions
        {
            ApiBaseUri = CreateApiBaseUri(configuredApiBaseUrl),
            BotDisplayNumber =
                FirstNonBlank(
                    configuration["BotDisplayNumber"],
                    configuration["PickupPal:BotDisplayNumber"]
                ) ?? DefaultBotDisplayNumber,
            
            BotUri = CreateUri(
                FirstNonBlank(configuration["BotUri"], configuration["PickupPal:BotUri"])
                    ?? DefaultBotUri,
                nameof(BotUri)
            ),
            TermsUri = CreateUri(
                FirstNonBlank(configuration["TermsUri"], configuration["PickupPal:TermsUri"])
                    ?? DefaultTermsUri,
                nameof(TermsUri)
            ),
            PrivacyPolicyUri = CreateUri(
                FirstNonBlank(configuration["PrivacyPolicyUri"], configuration["PickupPal:PrivacyPolicyUri"])
                    ?? DefaultPrivacyPolicyUri,
                nameof(PrivacyPolicyUri)
            ),
            CallbackUri = CreateUri(
                FirstNonBlank(configuration["CallbackUri"], configuration["PickupPal:CallbackUri"])
                    ?? DefaultCallbackUri,
                nameof(CallbackUri)
            ),
            AppLinkBaseUri = CreateUri(
                FirstNonBlank(configuration["AppLinkBaseUri"], configuration["PickupPal:AppLinkBaseUri"])
                    ?? DefaultAppLinkBaseUri,
                nameof(AppLinkBaseUri)
            ),
        };
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static Uri CreateApiBaseUri(string value)
    {
        var uri = CreateUri(value, nameof(ApiBaseUri));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{nameof(ApiBaseUri)} must not include query string or fragment values."
            );
        }

        var builder = new UriBuilder(uri);
        if (string.IsNullOrWhiteSpace(builder.Path) || builder.Path == "/")
        {
            builder.Path = "api/";
        }
        else if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static Uri CreateUri(string value, string optionName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{optionName} must be an absolute URI.");
        }

        return uri;
    }
}
