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
    public const string DefaultBotUri = "https://wa.me/16502205416";
    public const string DefaultSignupUri = "https://pickuppal.app/";
    public const string DefaultCallbackUri = "southbaysoccer://auth/whatsapp";

    public Uri ApiBaseUri { get; init; } = new(DefaultApiBaseUrl);
    public string BotDisplayNumber { get; init; } = DefaultBotDisplayNumber;
    public Uri BotUri { get; init; } = new(DefaultBotUri);
    public Uri SignupUri { get; init; } = new(DefaultSignupUri);
    public Uri CallbackUri { get; init; } = new(DefaultCallbackUri);

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
            SignupUri = CreateUri(
                FirstNonBlank(configuration["SignupUri"], configuration["PickupPal:SignupUri"])
                    ?? DefaultSignupUri,
                nameof(SignupUri)
            ),
            CallbackUri = CreateUri(
                FirstNonBlank(configuration["CallbackUri"], configuration["PickupPal:CallbackUri"])
                    ?? DefaultCallbackUri,
                nameof(CallbackUri)
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
