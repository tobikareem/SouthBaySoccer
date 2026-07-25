using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiProfileClient(HttpClient httpClient) : IProfileClient
{
    public Task<PlayerProfileDto?> GetProfileAsync(
        Guid playerId,
        CancellationToken cancellationToken) =>
        GetProfileByPathAsync($"profiles/{playerId}", cancellationToken);

    public async Task<PlayerProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("profiles/me", cancellationToken);
            response.EnsureSuccessStatusCode();
            var profile = await response.Content.ReadFromJsonAsync<MyProfileResponse>(
                cancellationToken: cancellationToken);

            return profile is null ? null : ToPlayerProfileDto(profile);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<PlayerProfileDto?> GetProfileByPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PlayerProfileDto>(
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static PlayerProfileDto ToPlayerProfileDto(MyProfileResponse profile) =>
        new(
            profile.PlayerProfileId,
            profile.DisplayName,
            profile.PreferredPosition,
            ToInitials(profile.DisplayName),
            profile.CareerStats ?? new CareerStatsDto(0, 0, 0, 0, 0, 0),
            profile.RecentForm ?? Array.Empty<MatchResult>(),
            profile.IsGuest ? "Guest profile" : null,
            profile.Role);

    private static string ToInitials(string displayName)
    {
        var initials = string.Concat(
            displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(initials) ? "SB" : initials;
    }
}
