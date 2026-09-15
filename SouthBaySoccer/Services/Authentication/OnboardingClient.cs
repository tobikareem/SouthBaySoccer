using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Authentication;

public sealed class OnboardingClient(HttpClient httpClient) : IOnboardingClient
{
    public async Task<PhoneSignInStartResponse> BeginPhoneSignInAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "auth/pickuppal/phone/sign-in",
            new SignInByPhoneRequest(phoneNumber),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        // Until the backend ships verificationRequired (M13.5) this endpoint returns a bare token
        // set. Accept both shapes so the client can ship first.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (root.TryGetProperty("verificationRequired", out _))
        {
            return root.Deserialize<PhoneSignInStartResponse>(JsonOptions)
                   ?? throw new InvalidOperationException("The sign-in service returned an empty response.");
        }

        var tokens = root.Deserialize<AuthenticationTokensResponse>(JsonOptions)
                     ?? throw new InvalidOperationException("The sign-in service returned an empty response.");
        return new PhoneSignInStartResponse(false, null, null, tokens);
    }

    public Task<AuthenticationTokensResponse> CompleteLoginAsync(string token, bool rememberDevice, CancellationToken cancellationToken) =>
        SendTokenRequestAsync<AuthenticationTokensResponse>(
            "auth/pickuppal/login/complete",
            new CompleteWhatsAppLoginRequest(token, rememberDevice),
            cancellationToken);

    public Task<RegistrationTokenValidationResponse> ValidateRegistrationAsync(string token, CancellationToken cancellationToken) =>
        SendTokenRequestAsync<RegistrationTokenValidationResponse>(
            "auth/pickuppal/register/validate",
            new ValidateRegistrationTokenRequest(token),
            cancellationToken);

    public async Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken)
    {
        // Email travels in the body, never the query string.
        using var response = await httpClient.PostAsJsonAsync(
            "auth/pickuppal/register/email-availability",
            new CheckEmailAvailabilityRequest(email),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CheckEmailAvailabilityResponse>(cancellationToken: cancellationToken)
                     ?? throw new InvalidOperationException("The sign-up service returned an empty response.");
        return result.IsAvailable;
    }

    public Task<RegistrationCompletedResponse> RegisterAsync(RegisterWithWhatsAppRequest request, CancellationToken cancellationToken) =>
        SendTokenRequestAsync<RegistrationCompletedResponse>("auth/pickuppal/register", request, cancellationToken);

    public async Task<string> GetTermsVersionAsync(CancellationToken cancellationToken)
    {
        var result = await httpClient.GetFromJsonAsync<TermsVersionResponse>("auth/terms/current", cancellationToken);
        return result?.Version ?? throw new InvalidOperationException("The terms version is unavailable.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Token-bearing calls. Non-success responses may surface either as a status code (bare client)
    /// or as an <see cref="HttpRequestException"/> thrown by <c>ApiExceptionHandler</c> in the
    /// configured pipeline; both paths map to <see cref="OnboardingTokenException"/>.
    /// </summary>
    private async Task<TResponse> SendTokenRequestAsync<TResponse>(string route, object body, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(route, body, cancellationToken);
        }
        catch (ApiRequestException ex) when (MapTokenFailure(ex.ProblemType) is { } failure)
        {
            // Behind ApiExceptionHandler the response is already consumed; the Functions identify
            // token outcomes by problem type, so a bare 404 for a missing route is NOT a token failure.
            throw new OnboardingTokenException(failure);
        }

        using (response)
        {
            if (MapTokenFailure(response.StatusCode) is { } failure)
            {
                throw new OnboardingTokenException(failure);
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("The sign-up service returned an empty response.");
        }
    }

    /// <summary>Problem-type prefix the Functions use for every token outcome.</summary>
    public const string TokenProblemTypePrefix = "https://southbaysoccer/problems/onboarding-token-";

    private static OnboardingTokenFailure? MapTokenFailure(string? problemType)
    {
        if (problemType is null || !problemType.StartsWith(TokenProblemTypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return problemType[TokenProblemTypePrefix.Length..].ToLowerInvariant() switch
        {
            "expired" => OnboardingTokenFailure.Expired,
            "invalid" => OnboardingTokenFailure.Invalid,
            "already-registered" => OnboardingTokenFailure.AlreadyRegistered,
            "mismatch" => OnboardingTokenFailure.Mismatch,
            _ => null
        };
    }

    // Without ApiExceptionHandler in the pipeline (bare client) fall back to the status codes the
    // Functions use: 410 expired, 409 already registered, 403 another user, 404 unknown token.
    private static OnboardingTokenFailure? MapTokenFailure(HttpStatusCode? statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Gone => OnboardingTokenFailure.Expired,
            HttpStatusCode.Conflict => OnboardingTokenFailure.AlreadyRegistered,
            HttpStatusCode.Forbidden => OnboardingTokenFailure.Mismatch,
            HttpStatusCode.NotFound => OnboardingTokenFailure.Invalid,
            _ => null
        };
}
