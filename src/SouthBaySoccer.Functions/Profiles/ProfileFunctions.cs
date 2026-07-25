using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Profiles;

public sealed class ProfileFunctions(
    GetMyProfileQueryHandler getMyProfileHandler,
    GetPlayerProfileQueryHandler getPlayerProfileHandler,
    UpdateMyProfileCommandHandler updateMyProfileHandler,
    CreateGuestProfileCommandHandler createGuestProfileHandler,
    CreateProfileMergeCommandHandler createProfileMergeHandler)
{
    [Function(nameof(GetMyProfile))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "profiles/me")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await getMyProfileHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetPlayerProfile))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetPlayerProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "profiles/{playerProfileId:guid}")] HttpRequestData request,
        Guid playerProfileId,
        CancellationToken cancellationToken)
    {
        var result = await getPlayerProfileHandler.HandleAsync(playerProfileId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(UpdateMyProfile))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> UpdateMyProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "profiles/me")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<UpdateMyProfileRequest>(request, cancellationToken);
        var result = await updateMyProfileHandler.HandleAsync(
            new UpdateMyProfileCommand(
                body.DisplayName,
                body.PreferredPosition,
                body.PhotoUri,
                ToModel(body.EmergencyContact)),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(CreateGuestProfile))]
    [RequirePolicy(AuthenticationPolicies.CanManagePlayers)]
    public async Task<HttpResponseData> CreateGuestProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "profiles/guests")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateGuestProfileRequest>(request, cancellationToken);
        var result = await createGuestProfileHandler.HandleAsync(
            new CreateGuestProfileCommand(
                body.DisplayName,
                body.PreferredPosition,
                body.PhotoUri,
                ToModel(body.EmergencyContact)),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.Created, ToResponse(result), cancellationToken);
    }

    [Function(nameof(CreateProfileMerge))]
    [RequirePolicy(AuthenticationPolicies.CanManagePlayers)]
    public async Task<HttpResponseData> CreateProfileMerge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "profiles/merges")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateProfileMergeRequest>(request, cancellationToken);
        var result = await createProfileMergeHandler.HandleAsync(
            new CreateProfileMergeCommand(body.SourceGuestPlayerProfileId, body.TargetPlayerProfileId),
            cancellationToken);

        return await WriteJsonAsync(
            request,
            HttpStatusCode.Created,
            new ProfileMergeResponse(
                result.ProfileMergeId,
                result.SourceGuestPlayerProfileId,
                result.TargetPlayerProfileId,
                result.Status),
            cancellationToken);
    }

    private static EmergencyContactModel? ToModel(EmergencyContactRequest? request) =>
        request is null ? null : new EmergencyContactModel(request.Name, request.PhoneNumber, request.Relationship);

    private static MyProfileResponse ToResponse(PlayerProfileModel profile) =>
        new(
            profile.PlayerProfileId,
            profile.IdentityUserId,
            profile.DisplayName,
            profile.PreferredPosition,
            profile.PhotoUri,
            profile.IsGuest,
            profile.Role,
            profile.EmergencyContact is null
                ? null
                : new EmergencyContactResponse(
                    profile.EmergencyContact.Name,
                    profile.EmergencyContact.MaskedPhoneNumber,
                    profile.EmergencyContact.Relationship));

    private static PlayerProfileDto ToResponse(PlayerProfileDetailModel profile) =>
        new(
            profile.PlayerProfileId,
            profile.DisplayName,
            profile.PreferredPosition,
            profile.Initials,
            new CareerStatsDto(
                profile.CareerStats.Matches,
                profile.CareerStats.Goals,
                profile.CareerStats.Assists,
                profile.CareerStats.AverageRating,
                profile.CareerStats.MvpAwards,
                profile.CareerStats.Likes,
                profile.CareerStats.Wins,
                profile.CareerStats.Losses),
            profile.RecentForm.Select(ToResponse).ToArray(),
            profile.PendingConfirmationNote,
            profile.Role);

    private static MatchResult ToResponse(PlayerProfileRecentFormOutcome outcome) =>
        outcome switch
        {
            PlayerProfileRecentFormOutcome.Win => MatchResult.Win,
            PlayerProfileRecentFormOutcome.Draw => MatchResult.Draw,
            PlayerProfileRecentFormOutcome.Loss => MatchResult.Loss,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported recent form outcome."),
        };

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<T>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        return body;
    }

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
