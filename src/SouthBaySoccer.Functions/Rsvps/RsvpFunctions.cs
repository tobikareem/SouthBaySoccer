using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Contracts.Rsvps;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Rsvps;

public sealed class RsvpFunctions(
    SubmitRsvpCommandHandler submitRsvpHandler,
    CancelRsvpCommandHandler cancelRsvpHandler,
    GetMyRsvpQueryHandler getMyRsvpHandler,
    GetSessionRosterQueryHandler getSessionRosterHandler,
    AdminOverrideRsvpCommandHandler adminOverrideRsvpHandler,
    CheckInPlayerCommandHandler checkInPlayerHandler,
    RecordNoShowsCommandHandler recordNoShowsHandler,
    IdempotentRequestExecutor idempotentRequestExecutor)
{
    [Function(nameof(SubmitRsvp))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> SubmitRsvp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/rsvp")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<SubmitRsvpRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(SubmitRsvp),
            GetIdempotencyKey(request),
            new { sessionId, body.Status },
            async token =>
            {
                var result = await submitRsvpHandler.HandleAsync(
                    new SubmitRsvpCommand(sessionId, ParseEnum<RsvpStatus>(body.Status, nameof(body.Status))),
                    token);
                return new IdempotentResponse<RsvpResponseDto>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(CancelRsvp))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> CancelRsvp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "sessions/{sessionId:guid}/rsvp")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(CancelRsvp),
            GetIdempotencyKey(request),
            new { sessionId },
            async token =>
            {
                var result = await cancelRsvpHandler.HandleAsync(new CancelRsvpCommand(sessionId), token);
                return new IdempotentResponse<RsvpResponseDto>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(GetMyRsvp))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyRsvp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/{sessionId:guid}/rsvp/me")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await getMyRsvpHandler.HandleAsync(sessionId, cancellationToken);
        return result is null
            ? request.CreateResponse(HttpStatusCode.NoContent)
            : await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetSessionRoster))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetSessionRoster(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/{sessionId:guid}/roster")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var roster = await getSessionRosterHandler.HandleAsync(sessionId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(roster), cancellationToken);
    }

    [Function(nameof(AdminOverrideRsvp))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> AdminOverrideRsvp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/rsvp/admin-override")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<AdminOverrideRsvpRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(AdminOverrideRsvp),
            GetIdempotencyKey(request),
            new { sessionId, body.PlayerProfileId, body.Reason },
            async token =>
            {
                var result = await adminOverrideRsvpHandler.HandleAsync(
                    new AdminOverrideRsvpCommand(sessionId, body.PlayerProfileId, body.Reason),
                    token);
                return new IdempotentResponse<RsvpResponseDto>(HttpStatusCode.Created, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(CheckInPlayer))]
    [RequirePolicy(AuthenticationPolicies.CanCheckInPlayers)]
    public async Task<HttpResponseData> CheckInPlayer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/check-ins")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CheckInPlayerRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(CheckInPlayer),
            GetIdempotencyKey(request),
            new { sessionId, body.PlayerProfileId, body.Outcome, body.LateOverrideReason },
            async token =>
            {
                var result = await checkInPlayerHandler.HandleAsync(
                    new CheckInPlayerCommand(sessionId, body.PlayerProfileId, ParseEnum<AttendanceOutcome>(body.Outcome, nameof(body.Outcome)), body.LateOverrideReason),
                    token);
                return new IdempotentResponse<CheckInResponseDto>(HttpStatusCode.Created, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(RecordNoShows))]
    [RequirePolicy(AuthenticationPolicies.CanCheckInPlayers)]
    public async Task<HttpResponseData> RecordNoShows(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/check-ins/no-shows")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(RecordNoShows),
            GetIdempotencyKey(request),
            new { sessionId },
            async token =>
            {
                var result = await recordNoShowsHandler.HandleAsync(new RecordNoShowsCommand(sessionId), token);
                return new IdempotentResponse<NoShowResponseDto>(HttpStatusCode.OK, new NoShowResponseDto(result.SessionId, result.RecordedCount));
            },
            cancellationToken);
    }

    private static Contracts.Rosters.RosterDto ToResponse(SessionRosterModel roster) =>
        new(
            roster.SessionId,
            roster.Going.Select(member => new Contracts.Rosters.RosterEntryDto(
                ToPlayerSummary(member),
                member.IsCurrentPlayer)).ToArray(),
            roster.Waitlist.Select(member => new Contracts.Rosters.WaitlistEntryDto(
                ToPlayerSummary(member),
                member.WaitlistPosition ?? 0)).ToArray());

    private static Contracts.Players.PlayerSummaryDto ToPlayerSummary(RosterMemberModel member) =>
        new(
            member.PlayerProfileId ?? Guid.Empty,
            member.DisplayName,
            ToInitials(member.DisplayName),
            member.PreferredPosition,
            member.IsGuest);

    private static string ToInitials(string displayName)
    {
        var initials = string.Concat(
            displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(initials) ? "SB" : initials;
    }

    private static RsvpResponseDto ToResponse(RsvpResultModel result) =>
        new(
            result.SessionId,
            result.PlayerProfileId,
            result.State,
            result.RsvpResponseId,
            result.WaitlistEntryId,
            result.WaitlistPosition,
            result.PromotedPlayerProfileId);

    private static CheckInResponseDto ToResponse(CheckInResultModel result) =>
        new(
            result.CheckInId,
            result.SessionId,
            result.PlayerProfileId,
            result.CheckedInByPlayerProfileId,
            result.CheckedInAtUtc,
            result.Outcome,
            result.IsLateOverride,
            result.AdminOverrideId,
            result.LateOverrideReason);

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            [propertyName] = [$"'{value}' is not supported."],
        });
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
