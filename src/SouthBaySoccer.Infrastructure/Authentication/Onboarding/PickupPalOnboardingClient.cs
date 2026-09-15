using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;

namespace SouthBaySoccer.Infrastructure.Authentication.Onboarding;

/// <summary>
/// HTTP client for the Pickup Pal account-creation, login-token, and deletion surface.
/// <para>
/// <b>URI-logging ban (same as <see cref="PickupPalUserClient"/>):</b> the external contract puts
/// the registration token in a query string (<c>validate?token=</c>) and the email in the path
/// (<c>api/users/email/{email}</c>). Both are forced by Pickup Pal and cannot be changed here, so
/// this client takes no <c>ILogger</c>, attaches no message handlers, and nothing may ever record
/// its outbound request URIs. Passwords and tokens travel only in request bodies otherwise.
/// </para>
/// <para>
/// Routes come from <see cref="PickupPalRouteOptions"/>; <c>LoginRedeem</c> and <c>DeleteUser</c>
/// are unconfirmed placeholders until the Pickup Pal developer confirms M13.0.
/// </para>
/// </summary>
public sealed class PickupPalOnboardingClient(HttpClient httpClient, IOptions<PickupPalApiOptions> options)
    : IPickupPalOnboardingClient
{
    private const string UnavailableMessage = "Pickup Pal is unavailable right now. Try again later.";

    public async Task<RegistrationTokenValidation> ValidateRegistrationTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var route = $"{options.Value.Routes.RegisterValidate}?token={Uri.EscapeDataString(token)}";

        using var response = await SendAsync(HttpMethod.Get, route, content: null, cancellationToken);
        if (IsServerFailure(response))
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        var payload = await ReadJsonAsync<ValidateTokenPayload>(response, cancellationToken);
        if (payload is null || !payload.Valid)
        {
            var reason = payload?.Reason?.Trim();
            var status = string.Equals(reason, "expired", StringComparison.OrdinalIgnoreCase)
                ? RegistrationTokenStatus.Expired
                : RegistrationTokenStatus.Invalid;
            return new RegistrationTokenValidation(status, null);
        }

        var digits = string.IsNullOrWhiteSpace(payload.PhoneNumber)
            ? null
            : new string(payload.PhoneNumber.Where(char.IsDigit).ToArray());
        return new RegistrationTokenValidation(RegistrationTokenStatus.Valid, digits);
    }

    public async Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken = default)
    {
        var route = options.Value.Routes.EmailLookup.Replace(
            "{email}",
            Uri.EscapeDataString(email),
            StringComparison.Ordinal);

        using var response = await SendAsync(HttpMethod.Get, route, content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (response.IsSuccessStatusCode)
        {
            return false;
        }

        throw new ApplicationServiceUnavailableException(UnavailableMessage);
    }

    public async Task<PickupPalUser> RegisterWithTokenAsync(
        PickupPalRegistrationRequest registration,
        CancellationToken cancellationToken = default)
    {
        var body = new RegisterPayload(
            registration.Token,
            registration.FirstName,
            registration.LastName,
            registration.Email,
            registration.Password,
            registration.TermsVersion,
            registration.TermsAcceptedAtUtc);

        using var response = await SendAsync(
            HttpMethod.Post,
            options.Value.Routes.RegisterWithToken,
            JsonContent.Create(body),
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return ToPickupPalUser(await ReadJsonAsync<UserPayload>(response, cancellationToken));
        }

        throw await ToOnboardingExceptionAsync(response, cancellationToken);
    }

    public async Task<PickupPalUser> RedeemLoginTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            options.Value.Routes.LoginRedeem,
            JsonContent.Create(new LoginRedeemPayload(token)),
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return ToPickupPalUser(await ReadJsonAsync<UserPayload>(response, cancellationToken));
        }

        throw await ToOnboardingExceptionAsync(response, cancellationToken);
    }

    public async Task DeleteUserAsync(string pickupPalUserId, CancellationToken cancellationToken = default)
    {
        var route = options.Value.Routes.DeleteUser.Replace(
            "{id}",
            Uri.EscapeDataString(pickupPalUserId),
            StringComparison.Ordinal);

        using var response = await SendAsync(HttpMethod.Delete, route, content: null, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        // Anything else (missing API key, unexpected 4xx, 5xx) is retryable from the outbox.
        throw new ApplicationServiceUnavailableException(UnavailableMessage);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        using var request = new HttpRequestMessage(method, route) { Content = content };
        var apiKey = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation(options.Value.ApiKeyHeaderName, apiKey);
        }

        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException without the caller's token set.
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }
    }

    private static async Task<Exception> ToOnboardingExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (IsServerFailure(response))
        {
            return new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken);
        return new PickupPalOnboardingException(Classify(response.StatusCode, message), message);
    }

    /// <summary>
    /// Maps Pickup Pal's literal error strings (contract section 4) to failures. Unknown 4xx strings
    /// fall back on status and keywords so an unconfirmed login-redemption body still classifies.
    /// </summary>
    private static PickupPalOnboardingFailure Classify(HttpStatusCode statusCode, string? message)
    {
        var text = message ?? string.Empty;
        if (Contains(text, "expired"))
        {
            return PickupPalOnboardingFailure.TokenExpired;
        }

        if (Contains(text, "token") || statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Unauthorized)
        {
            return PickupPalOnboardingFailure.TokenInvalid;
        }

        if (Contains(text, "email already exists"))
        {
            return PickupPalOnboardingFailure.EmailAlreadyRegistered;
        }

        if (Contains(text, "phone number already exists"))
        {
            return PickupPalOnboardingFailure.PhoneAlreadyRegistered;
        }

        return PickupPalOnboardingFailure.Validation;
    }

    /// <summary>
    /// Reads both Pickup Pal error shapes: <c>{ "error": "text" }</c> on 4xx and
    /// <c>{ "error": { "message": "text", "status": n } }</c> from the unhandled-error handler, plus
    /// the <c>{ "valid": false, "reason": "..." }</c> shape the validate route uses for a missing token.
    /// </summary>
    private static async Task<string?> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("error", out var error))
            {
                return error.ValueKind switch
                {
                    JsonValueKind.String => error.GetString(),
                    JsonValueKind.Object when error.TryGetProperty("message", out var nested) &&
                                              nested.ValueKind == JsonValueKind.String => nested.GetString(),
                    _ => null,
                };
            }

            return root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsServerFailure(HttpResponseMessage response) => (int)response.StatusCode >= 500;

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static PickupPalUser ToPickupPalUser(UserPayload? payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.PhoneNumber))
        {
            throw new InvalidOperationException("Pickup Pal returned an incomplete user profile.");
        }

        return new PickupPalUser(
            payload.Id.Trim(),
            string.IsNullOrWhiteSpace(payload.Email) ? null : payload.Email.Trim(),
            payload.PhoneNumber.Trim(),
            payload.FirstName,
            payload.LastName,
            payload.NickName,
            payload.ProfilePicture,
            Array.Empty<string>(),
            payload.UpdatedAt);
    }

    private sealed record ValidateTokenPayload(
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("phoneNumber")] string? PhoneNumber,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record RegisterPayload(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("firstName")] string FirstName,
        [property: JsonPropertyName("lastName")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("termsVersion")] string TermsVersion,
        [property: JsonPropertyName("termsAcceptedAt")] DateTime TermsAcceptedAt);

    private sealed record LoginRedeemPayload([property: JsonPropertyName("token")] string Token);

    private sealed record UserPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phoneNumber")] string? PhoneNumber,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("nickName")] string? NickName,
        [property: JsonPropertyName("profilePicture")] string? ProfilePicture,
        [property: JsonPropertyName("updatedAt")] DateTime? UpdatedAt);
}
