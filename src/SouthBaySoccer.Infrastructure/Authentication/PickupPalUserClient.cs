using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// HTTP client for resolving users from the Pickup Pal API.
/// </summary>
public sealed class PickupPalUserClient(HttpClient httpClient, IOptions<PickupPalApiOptions> options)
    : IPickupPalUserClient
{
    public async Task<PickupPalUser?> FindByPhoneAsync(
        string phoneNumberDigits,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberDigits))
        {
            return null;
        }

        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        using var response = await httpClient.GetAsync(
            $"api/users/phone/{Uri.EscapeDataString(phoneNumberDigits)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PickupPalUserResponse>(
            cancellationToken: cancellationToken);

        if (payload is null || !string.IsNullOrWhiteSpace(payload.Error))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.PhoneNumber))
        {
            throw new InvalidOperationException("Pickup Pal returned an incomplete user profile.");
        }

        return await FindByIdAsync(payload.Id.Trim(), payload, cancellationToken);
    }

    private async Task<PickupPalUser> FindByIdAsync(
        string pickupPalUserId,
        PickupPalUserResponse fallback,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"api/users/{Uri.EscapeDataString(pickupPalUserId)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ToPickupPalUser(fallback);
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PickupPalUserResponse>(
            cancellationToken: cancellationToken);

        return ToPickupPalUser(payload ?? fallback);
    }

    private static PickupPalUser ToPickupPalUser(PickupPalUserResponse payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.PhoneNumber))
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
            GetSoccerPositions(payload.UserInfo),
            payload.UpdatedAt);
    }

    private static IReadOnlyList<string> GetSoccerPositions(PickupPalUserInfoResponse? userInfo)
    {
        return userInfo?.SportsInfo?
            .Where(x => x.IsActive && string.Equals(x.Sport, "SOCCER", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Positions ?? Array.Empty<string>())
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Select(position => position.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private sealed record PickupPalUserResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phoneNumber")] string? PhoneNumber,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("nickName")] string? NickName,
        [property: JsonPropertyName("profilePicture")] string? ProfilePicture,
        [property: JsonPropertyName("updatedAt")] DateTime? UpdatedAt,
        [property: JsonPropertyName("userInfo")] PickupPalUserInfoResponse? UserInfo,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record PickupPalUserInfoResponse(
        [property: JsonPropertyName("sportsInfo")] IReadOnlyList<PickupPalSportsInfoResponse>? SportsInfo);

    private sealed record PickupPalSportsInfoResponse(
        [property: JsonPropertyName("sport")] string? Sport,
        [property: JsonPropertyName("positions")] IReadOnlyList<string>? Positions,
        [property: JsonPropertyName("isActive")] bool IsActive);
}
