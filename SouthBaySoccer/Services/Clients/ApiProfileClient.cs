using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiProfileClient(HttpClient httpClient) : IProfileClient
{
    public Task<PlayerProfileDto?> GetProfileAsync(
        Guid playerId,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlayerProfileDto?>(null);

    public async Task<PlayerProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("profiles/me", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<MyProfileResponse>(
            cancellationToken: cancellationToken);

        return profile is null ? null : ToPlayerProfileDto(profile);
    }

    private static PlayerProfileDto ToPlayerProfileDto(MyProfileResponse profile) =>
        new(
            profile.PlayerProfileId,
            profile.DisplayName,
            profile.PreferredPosition,
            ToInitials(profile.DisplayName),
            new CareerStatsDto(0, 0, 0, 0, 0, 0),
            Array.Empty<MatchResult>(),
            profile.IsGuest ? "Guest profile" : null);

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
