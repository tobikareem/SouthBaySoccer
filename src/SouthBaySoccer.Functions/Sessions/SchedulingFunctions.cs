using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Sessions;

public sealed class SchedulingFunctions(
    CreateSeasonCommandHandler createSeasonHandler,
    ListSeasonsQueryHandler listSeasonsHandler,
    CreateVenueCommandHandler createVenueHandler,
    ListVenuesQueryHandler listVenuesHandler,
    CreateSessionCommandHandler createSessionHandler,
    ListUpcomingSessionsQueryHandler listUpcomingSessionsHandler,
    CancelSessionCommandHandler cancelSessionHandler,
    CreateRecurrenceRuleCommandHandler createRecurrenceRuleHandler,
    CreateSessionOccurrenceCommandHandler createSessionOccurrenceHandler)
{
    [Function(nameof(ListSeasons))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> ListSeasons(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "seasons")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var seasons = await listSeasonsHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, seasons.Select(ToResponse).ToArray(), cancellationToken);
    }

    [Function(nameof(CreateSeason))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateSeason(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "seasons")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateSeasonRequest>(request, cancellationToken);
        var season = await createSeasonHandler.HandleAsync(
            new CreateSeasonCommand(body.Name, body.StartsAtUtc, body.EndsAtUtc),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.Created, ToResponse(season), cancellationToken);
    }

    [Function(nameof(ListVenues))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> ListVenues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "venues")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var venues = await listVenuesHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, venues.Select(ToResponse).ToArray(), cancellationToken);
    }

    [Function(nameof(CreateVenue))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateVenue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "venues")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateVenueRequest>(request, cancellationToken);
        var venue = await createVenueHandler.HandleAsync(
            new CreateVenueCommand(body.Name, body.Locality, body.Address),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.Created, ToResponse(venue), cancellationToken);
    }

    [Function(nameof(ListUpcomingSessions))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> ListUpcomingSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var sessions = await listUpcomingSessionsHandler.HandleAsync(cancellationToken: cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, sessions.Select(ToResponse).ToArray(), cancellationToken);
    }

    [Function(nameof(CreateSession))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateSessionAdminRequest>(request, cancellationToken);
        var session = await createSessionHandler.HandleAsync(
            new SouthBaySoccer.Application.Features.Scheduling.CreateSessionCommand(
                body.SeasonId,
                body.VenueId,
                body.Title,
                body.Format,
                body.Capacity,
                body.TeamCount,
                body.StartsAtUtc,
                body.CheckInOpensAtUtc,
                body.CheckInClosesAtUtc,
                body.RsvpDeadlineUtc,
                body.RecurrenceRuleId,
                body.OccurrenceKey),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.Created, ToResponse(session), cancellationToken);
    }

    [Function(nameof(CancelSession))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CancelSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/cancel")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CancelSessionRequest>(request, cancellationToken);
        var session = await cancelSessionHandler.HandleAsync(new CancelSessionCommand(sessionId, body.Reason), cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(session), cancellationToken);
    }

    [Function(nameof(CreateRecurrenceRule))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateRecurrenceRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recurrence-rules")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateRecurrenceRuleRequest>(request, cancellationToken);
        var recurrenceRule = await createRecurrenceRuleHandler.HandleAsync(
            new CreateRecurrenceRuleCommand(body.Name, body.TimeZoneId, body.Rule),
            cancellationToken);

        return await WriteJsonAsync(
            request,
            HttpStatusCode.Created,
            new RecurrenceRuleResponse(
                recurrenceRule.RecurrenceRuleId,
                recurrenceRule.Name,
                recurrenceRule.TimeZoneId,
                recurrenceRule.Rule),
            cancellationToken);
    }

    [Function(nameof(CreateSessionOccurrence))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateSessionOccurrence(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/occurrences")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateSessionOccurrenceRequest>(request, cancellationToken);
        var session = await createSessionOccurrenceHandler.HandleAsync(
            new CreateSessionOccurrenceCommand(
                body.RecurrenceRuleId,
                body.SeasonId,
                body.VenueId,
                body.OccurrenceStartsAtUtc,
                body.Title,
                body.Format,
                body.Capacity,
                body.TeamCount,
                body.CheckInOpensAtUtc,
                body.CheckInClosesAtUtc,
                body.RsvpDeadlineUtc),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.Created, ToResponse(session), cancellationToken);
    }

    private static SeasonResponse ToResponse(SeasonModel season) =>
        new(season.SeasonId, season.Name, season.StartsAtUtc, season.EndsAtUtc);

    private static VenueResponse ToResponse(VenueModel venue) =>
        new(venue.VenueId, venue.Name, venue.Locality, venue.Address, venue.MapsProviderReference);

    private static SessionAdminResponse ToResponse(SessionModel session) =>
        new(
            session.SessionId,
            session.SeasonId,
            session.VenueId,
            session.RecurrenceRuleId,
            session.Title,
            session.Format,
            session.Capacity,
            session.TeamCount,
            session.StartsAtUtc,
            session.CheckInOpensAtUtc,
            session.CheckInClosesAtUtc,
            session.RsvpDeadlineUtc,
            session.OccurrenceKey,
            session.Status);

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
