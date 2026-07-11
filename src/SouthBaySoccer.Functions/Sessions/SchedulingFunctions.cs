using System.Net;
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
        var query = GetQueryParameter(request.Url, "query");
        var venues = await listVenuesHandler.HandleAsync(cancellationToken);
        var filtered = string.IsNullOrWhiteSpace(query)
            ? venues
            : venues.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.Locality.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return await WriteJsonAsync(request, HttpStatusCode.OK, filtered.Select(ToResponse).ToArray(), cancellationToken);
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
        return session is null
            ? request.CreateResponse(HttpStatusCode.NotFound)
            : await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(session), cancellationToken);
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
        var localStart = ToLocal(session.StartsAtUtc);
        return new ManagedSessionDto(
            session.SessionId,
            session.Title,
            localStart.ToString("MMM d"),
            localStart.ToString("h:mm tt"),
            session.VenueName,
            session.Format,
            session.Capacity,
            session.Status);
    }

    private static ManagedSessionEditDto ToResponse(ManagedSessionEditModel session)
    {
        var localStart = ToLocal(session.StartsAtUtc);
        var localCheckInOpen = ToLocal(session.CheckInOpensAtUtc);
        var localCheckInClose = ToLocal(session.CheckInClosesAtUtc);
        var localRsvpDeadline = ToLocal(session.RsvpDeadlineUtc);
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
                localRsvpDeadline.TimeOfDay),
            string.Equals(session.Status, "Published", StringComparison.OrdinalIgnoreCase));
    }

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
            ToUtc(command.GameDateLocal, command.StartTimeLocal),
            ToUtc(command.GameDateLocal, command.CheckInOpenLocal),
            ToUtc(command.GameDateLocal, command.CheckInCloseLocal),
            ToUtc(command.GameDateLocal, command.RsvpDeadlineLocal ?? command.StartTimeLocal.Subtract(TimeSpan.FromHours(1))));

    private static UpdateSessionAdminCommand ToUpdateCommand(Guid sessionId, ContractCreateSessionCommand command) =>
        new(
            sessionId,
            command.VenueId,
            command.VenueName,
            command.Format,
            command.Capacity,
            command.TeamCount,
            ToUtc(command.GameDateLocal, command.StartTimeLocal),
            ToUtc(command.GameDateLocal, command.CheckInOpenLocal),
            ToUtc(command.GameDateLocal, command.CheckInCloseLocal),
            ToUtc(command.GameDateLocal, command.RsvpDeadlineLocal ?? command.StartTimeLocal.Subtract(TimeSpan.FromHours(1))));

    private static DateTime ToUtc(DateTime localDate, TimeSpan localTime)
    {
        var local = DateTime.SpecifyKind(localDate.Date + localTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, PacificTimeZone);
    }

    private static DateTime ToLocal(DateTime utc)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(normalized, PacificTimeZone);
    }

    private static TimeZoneInfo PacificTimeZone { get; } = FindPacificTimeZone();

    private static TimeZoneInfo FindPacificTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
    }

    private static string? GetQueryParameter(Uri url, string name)
    {
        var query = url.Query;
        if (string.IsNullOrWhiteSpace(query) || query.Length <= 1)
        {
            return null;
        }

        foreach (var pair in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(parts[0]);
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
        }

        return null;
    }
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


