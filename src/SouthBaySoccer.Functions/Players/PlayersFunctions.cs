using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Players;

public sealed class PlayersFunctions(GetPlayerDirectoryQueryHandler getPlayerDirectoryHandler)
{
    [Function(nameof(GetPlayerDirectory))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetPlayerDirectory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/directory")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await getPlayerDirectoryHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    private static PlayerDirectoryDto ToResponse(PlayerDirectoryModel directory) =>
        new(
            directory.Title,
            directory.Subtitle,
            directory.TotalPlayers,
            directory.Players.Select(ToResponse).ToArray());

    private static PlayerDirectoryEntryDto ToResponse(PlayerDirectoryEntryModel entry) =>
        new(
            new PlayerSummaryDto(
                entry.Player.Id,
                entry.Player.DisplayName,
                entry.Player.Initials,
                entry.Player.Position,
                entry.Player.IsGuest),
            entry.Subtitle,
            entry.Matches);

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken: cancellationToken);
        return response;
    }
}
