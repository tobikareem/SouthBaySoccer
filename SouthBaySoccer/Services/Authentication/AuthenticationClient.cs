using System.Net.Http.Json;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AuthenticationClient(HttpClient httpClient, PickupPalOptions options)
    : IAuthenticationClient
{
    public async Task<AuthenticationTokensResponse> SignInByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "auth/pickuppal/phone/sign-in",
            new SignInByPhoneRequest(phoneNumber),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticationTokensResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The sign-in service returned an empty response.");
    }
    public async Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var request = new RequestWhatsAppChallengeRequest(phoneNumber, options.CallbackUri.ToString());
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/auth/whatsapp/challenge",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RequestWhatsAppChallengeResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The sign-in service returned an empty response.");
    }

    public async Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken)
    {
        var request = new VerifyWhatsAppChallengeRequest(challengeToken, options.CallbackUri.ToString());
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/auth/whatsapp/verify",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticationTokensResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The sign-in service returned an empty response.");
    }

    public async Task<AuthenticationTokensResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/auth/refresh",
            new RefreshTokenRequest(refreshToken),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthenticationTokensResponse>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The sign-in service returned an empty response.");
    }
}
