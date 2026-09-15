namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Options for the Pickup Pal API integration. Bound from the <c>PickupPal</c> configuration section.
/// </summary>
public sealed class PickupPalApiOptions
{
    /// <summary>Gets or sets the Pickup Pal API base URL.</summary>
    public string BaseUrl { get; set; } = "https://pickuppal-bot-dev.up.railway.app";

    /// <summary>
    /// Gets or sets the bot API key presented on onboarding calls, when Pickup Pal has one
    /// (prerequisite M13.0). Absent until then; must come from user secrets or Key Vault.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the header the API key is sent in.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>Gets or sets the onboarding route templates, relative to <see cref="BaseUrl"/>.</summary>
    public PickupPalRouteOptions Routes { get; set; } = new();
}

/// <summary>
/// Route templates for the Pickup Pal onboarding surface. Confirmed routes come from
/// <c>documentation/pickuppal-account-creation-api.md</c>; the ones marked unconfirmed are
/// placeholders awaiting the Pickup Pal developer (M13.0) and exist so the real paths can be set
/// through configuration without a code change.
/// </summary>
public sealed class PickupPalRouteOptions
{
    /// <summary>Gets or sets the registration-token validation route (confirmed; token travels as a query parameter).</summary>
    public string RegisterValidate { get; set; } = "api/users/register/whatsapp/validate";

    /// <summary>Gets or sets the WhatsApp registration route (confirmed).</summary>
    public string RegisterWithToken { get; set; } = "api/users/register/whatsapp";

    /// <summary>
    /// Gets or sets the email lookup route (confirmed). <c>{email}</c> is replaced with the
    /// escaped email; a 404 means the email is available.
    /// </summary>
    public string EmailLookup { get; set; } = "api/users/email/{email}";

    /// <summary>Gets or sets the login-token redemption route. UNCONFIRMED placeholder pending M13.0.</summary>
    public string LoginRedeem { get; set; } = "api/users/login/whatsapp";

    /// <summary>
    /// Gets or sets the user deletion route. UNCONFIRMED placeholder pending M13.0; <c>{id}</c> is
    /// replaced with the escaped Pickup Pal user id.
    /// </summary>
    public string DeleteUser { get; set; } = "api/users/{id}";
}
