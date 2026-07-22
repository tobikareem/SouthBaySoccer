using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using ApplicationCreateSessionCommand = SouthBaySoccer.Application.Features.Scheduling.CreateSessionCommand;
using ContractCreateSessionCommand = SouthBaySoccer.Contracts.Sessions.CreateSessionCommand;

namespace SouthBaySoccer.Functions.Sessions;

public sealed class SchedulingFunctions(
    CreateSeasonCommandHandler createSeasonHandler,
    ListSeasonsQueryHandler listSeasonsHandler,
    CreateVenueCommandHandler createVenueHandler,
    ListVenuesQueryHandler listVenuesHandler,
    CreateSessionCommandHandler createSessionHandler,
    ListUpcomingSessionsQueryHandler listUpcomingSessionsHandler,
    CancelSessionCommandHandler cancelSessionHandler,
    DeleteSessionCommandHandler deleteSessionHandler,
    CreateRecurrenceRuleCommandHandler createRecurrenceRuleHandler,
    CreateSessionOccurrenceCommandHandler createSessionOccurrenceHandler,
    GetCreateSessionAdminDefaultsQueryHandler getCreateSessionAdminDefaultsHandler,
    ListManagedSessionsQueryHandler listManagedSessionsHandler,
    GetSessionForAdminEditQueryHandler getSessionForAdminEditHandler,
    CreateSessionDraftCommandHandler createSessionDraftHandler,
    UpdateSessionAdminCommandHandler updateSessionAdminHandler,
    PublishSessionCommandHandler publishSessionHandler,
    IdempotentRequestExecutor idempotentRequestExecutor)
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
        var query = QueryHelpers.ParseQuery(request.Url.Query);
        var venues = await listVenuesHandler.HandleAsync(ReadOptionalString(query, "query"), cancellationToken);
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

    [Function(nameof(GetCreateSessionDefaults))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> GetCreateSessionDefaults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/admin/create-defaults")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var defaults = await getCreateSessionAdminDefaultsHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(defaults), cancellationToken);
    }

    [Function(nameof(ListManagedSessions))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> ListManagedSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/admin/managed")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var sessions = await listManagedSessionsHandler.HandleAsync(cancellationToken: cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, sessions.Select(ToResponse).ToArray(), cancellationToken);
    }

    [Function(nameof(GetSessionForAdminEdit))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> GetSessionForAdminEdit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/{sessionId:guid}/admin-edit")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await getSessionForAdminEditHandler.HandleAsync(sessionId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(session), cancellationToken);
    }

    [Function(nameof(CreateSessionDraft))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateSessionDraft(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/drafts")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<ContractCreateSessionCommand>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(CreateSessionDraft),
            GetIdempotencyKey(request),
            body,
            async token =>
            {
                var result = await createSessionDraftHandler.HandleAsync(ToDraftCommand(body), token);
                return new IdempotentResponse<CreateSessionResult>(HttpStatusCode.Created, CreateSessionResult.Success(result.SessionId));
            },
            cancellationToken);
    }

    [Function(nameof(CreateSession))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> CreateSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateSessionAdminRequest>(request, cancellationToken);
        var session = await createSessionHandler.HandleAsync(
            new ApplicationCreateSessionCommand(
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

    [Function(nameof(UpdateSession))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> UpdateSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "sessions/{sessionId:guid}")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<ContractCreateSessionCommand>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(UpdateSession),
            GetIdempotencyKey(request),
            new { sessionId, body },
            async token =>
            {
                var result = await updateSessionAdminHandler.HandleAsync(ToUpdateCommand(sessionId, body), token);
                return new IdempotentResponse<CreateSessionResult>(HttpStatusCode.OK, CreateSessionResult.Success(result.SessionId));
            },
            cancellationToken);
    }

    [Function(nameof(PublishSession))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> PublishSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/publish")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(PublishSession),
            GetIdempotencyKey(request),
            new { sessionId },
            async token =>
            {
                var result = await publishSessionHandler.HandleAsync(sessionId, token);
                return new IdempotentResponse<CreateSessionResult>(HttpStatusCode.OK, CreateSessionResult.Success(result.SessionId));
            },
            cancellationToken);
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

    [Function(nameof(DeleteSession))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> DeleteSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "sessions/{sessionId:guid}")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await deleteSessionHandler.HandleAsync(new DeleteSessionCommand(sessionId), cancellationToken);
        return request.CreateResponse(HttpStatusCode.NoContent);
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

    private static VenueDto ToVenueDto(VenueModel venue) =>
        new(venue.VenueId, venue.Name, venue.Locality, true);

    private static CreateSessionDefaultsDto ToResponse(CreateSessionAdminDefaultsModel defaults) =>
        new(
            defaults.CanManageSessions,
            defaults.DefaultGameDateLocal,
            defaults.DefaultStartTimeLocal,
            defaults.CheckInLeadMinutes,
            defaults.CheckInCloseOffsetMinutes,
            defaults.Formats,
            defaults.DefaultFormatIndex,
            defaults.DefaultCapacity,
            defaults.MinimumCapacity,
            defaults.MaximumCapacity,
            defaults.TeamOptions,
            defaults.DefaultTeamIndex,
            ToVenueDto(defaults.SavedVenue),
            defaults.FeedLabel,
            defaults.DefaultRsvpDeadlineLocal);

    private static ManagedSessionDto ToResponse(ManagedSessionModel session)
    {
        var localStart = SessionAdminTimeZone.ToLocal(session.StartsAtUtc);
        return new ManagedSessionDto(
            session.SessionId,
            session.Title,
            localStart.ToString("MMM d", CultureInfo.InvariantCulture),
            localStart.ToString("h:mm tt", CultureInfo.InvariantCulture),
            session.VenueName,
            session.Format,
            session.Capacity,
            session.Status,
            string.Equals(session.Status, "Canceled", StringComparison.OrdinalIgnoreCase));
    }

    private static ManagedSessionEditDto ToResponse(ManagedSessionEditModel session)
    {
        var localStart = SessionAdminTimeZone.ToLocal(session.StartsAtUtc);
        var localCheckInOpen = SessionAdminTimeZone.ToLocal(session.CheckInOpensAtUtc);
        var localCheckInClose = SessionAdminTimeZone.ToLocal(session.CheckInClosesAtUtc);
        var localRsvpDeadline = SessionAdminTimeZone.ToLocal(session.RsvpDeadlineUtc);
        return new ManagedSessionEditDto(
            session.SessionId,
            new ContractCreateSessionCommand(
                DateTime.SpecifyKind(localStart.Date, DateTimeKind.Unspecified),
                localStart.TimeOfDay,
                localCheckInOpen.TimeOfDay,
                localCheckInClose.TimeOfDay,
                session.VenueId,
                session.VenueName,
                session.Format,
                session.Capacity,
                session.TeamCount,
                localRsvpDeadline.TimeOfDay,
                DayOffset(localCheckInOpen, localStart),
                DayOffset(localCheckInClose, localStart),
                DayOffset(localRsvpDeadline, localStart)),
            string.Equals(session.Status, "Published", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Days between <paramref name="local"/>'s calendar date and <paramref name="gameDayLocal"/>'s
    /// (negative when <paramref name="local"/> falls before the game day). Preserving this alongside
    /// the time-of-day is what lets a deadline set the evening before the game survive an edit
    /// round-trip instead of being coerced onto game day.
    /// </summary>
    private static int DayOffset(DateTime local, DateTime gameDayLocal) =>
        (local.Date - gameDayLocal.Date).Days;

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

    private static CreateSessionDraftCommand ToDraftCommand(ContractCreateSessionCommand command) =>
        new(
            command.VenueId,
            command.VenueName,
            command.Format,
            command.Capacity,
            command.TeamCount,
            SessionAdminTimeZone.ToUtc(command.GameDateLocal, command.StartTimeLocal),
            SessionAdminTimeZone.ToUtc(command.GameDateLocal.AddDays(command.CheckInOpenDayOffset), command.CheckInOpenLocal),
            SessionAdminTimeZone.ToUtc(command.GameDateLocal.AddDays(command.CheckInCloseDayOffset), command.CheckInCloseLocal),
            SessionAdminTimeZone.ToUtc(
                command.GameDateLocal.AddDays(command.RsvpDeadlineDayOffset),
                command.RsvpDeadlineLocal ?? command.StartTimeLocal.Subtract(TimeSpan.FromHours(1))));

    private static UpdateSessionAdminCommand ToUpdateCommand(Guid sessionId, ContractCreateSessionCommand command) =>
        new(
            sessionId,
            command.VenueId,
            command.VenueName,
            command.Format,
            command.Capacity,
            command.TeamCount,
            SessionAdminTimeZone.ToUtc(command.GameDateLocal, command.StartTimeLocal),
            SessionAdminTimeZone.ToUtc(command.GameDateLocal.AddDays(command.CheckInOpenDayOffset), command.CheckInOpenLocal),
            SessionAdminTimeZone.ToUtc(command.GameDateLocal.AddDays(command.CheckInCloseDayOffset), command.CheckInCloseLocal),
            SessionAdminTimeZone.ToUtc(
                command.GameDateLocal.AddDays(command.RsvpDeadlineDayOffset),
                command.RsvpDeadlineLocal ?? command.StartTimeLocal.Subtract(TimeSpan.FromHours(1))));

    private static string? ReadOptionalString(
        IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query,
        string key) =>
        query.TryGetValue(key, out var values) ? values.FirstOrDefault() : null;

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

    private static string GetIdempotencyKey(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("Idempotency-Key", out var values))
        {
            var value = values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            ["Idempotency-Key"] = ["The Idempotency-Key header is required for this operation."],
        });
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
