using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Rsvps;
using SouthBaySoccer.Contracts.Sessions;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// API-backed sessions client. The backend does not yet expose the dashboard/detail read
/// projections recorded in the Sprint 03 API-0 inventory (GET sessions/dashboard,
/// GET sessions/{sessionId}), so this client composes both screens from GET sessions plus the
/// caller's own RSVP state. Fields those projections would provide degrade explicitly rather than
/// faking data: venue names, going/waitlist counts, dues status, and the stats prompt stay
/// empty/zero/null until the backend endpoints exist, and only the featured session gets an RSVP
/// lookup (coming-up cards always read "Open" to avoid an N+1 request per card). Greeting and
/// CanManageSessions are fallbacks the sessions home page model rebuilds from the profile.
/// </summary>
public sealed class ApiSessionsClient(HttpClient httpClient, TimeProvider timeProvider) : ISessionsClient
{
    private const string PublishedStatus = "Published";
    private const string CanceledStatus = "Canceled";
    private const string CanceledLabel = "Cancelled";
    private const string GoingState = "Going";
    private const string YouAreGoingLabel = "You're going";
    private const string OpenLabel = "Open";

    // One key per session's join-waitlist operation, reused until the server acknowledges success,
    // so a retry after a dropped response replays the stored result instead of double-submitting
    // (same contract as ApiSessionAdminClient — see the idempotency note there).
    private readonly ConcurrentDictionary<Guid, string> _joinWaitlistIdempotencyKeysBySessionId = new();

    public async Task<SessionsDashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var upcoming = await GetPublishedUpcomingAsync(cancellationToken);
        var featured = upcoming.FirstOrDefault(session => !IsCanceled(session));

        SessionSummaryDto? featuredSummary = null;
        if (featured is not null)
        {
            var myRsvp = await GetMyRsvpAsync(featured.SessionId, cancellationToken);
            var statusLabel = myRsvp?.State == GoingState ? YouAreGoingLabel : OpenLabel;
            featuredSummary = ToSummary(featured, statusLabel, BuildRelativeLabel(featured.StartsAtUtc));
        }

        return new SessionsDashboardDto(
            "South Bay Soccer",
            "Welcome back",
            DuesStatus: string.Empty,
            featuredSummary,
            await GetPendingStatsPromptAsync(cancellationToken),
            "Coming up",
            "See schedule",
            upcoming
                .Where(session => session.SessionId != featured?.SessionId)
                .Select(session => ToSummary(
                    session,
                    IsCanceled(session) ? CanceledLabel : OpenLabel,
                    relativeLabel: null))
                .ToArray(),
            CanManageSessions: false);
    }

    public async Task<SessionDetailDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var upcoming = await GetPublishedUpcomingAsync(cancellationToken);
        var session = upcoming.FirstOrDefault(item => item.SessionId == sessionId);
        if (session is null)
        {
            return null;
        }

        var isCanceled = IsCanceled(session);
        var myRsvp = isCanceled ? null : await GetMyRsvpAsync(sessionId, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var localStart = ToLocal(session.StartsAtUtc);

        return new SessionDetailDto(
            session.SessionId,
            Eyebrow: session.Title,
            Venue: string.Empty,
            LocationLabel: $"{session.Format} pickup",
            session.Format,
            session.StartsAtUtc,
            DateTimeLabel: localStart.ToString("ddd MMM d · h:mm tt", CultureInfo.InvariantCulture),
            GoingCount: 0,
            session.Capacity,
            DeadlineLabel: BuildDeadlineLabel(session.RsvpDeadlineUtc, nowUtc),
            IsFull: false,
            IsRsvpAvailable: !isCanceled && nowUtc < session.RsvpDeadlineUtc,
            IsGoing: myRsvp?.State == GoingState,
            IsCanceled: isCanceled);
    }

    public Task<ClientCommandResult> JoinWaitlistAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            var key = _joinWaitlistIdempotencyKeysBySessionId.GetOrAdd(
                sessionId,
                static _ => NewIdempotencyKey());
            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/rsvp")
            {
                Content = JsonContent.Create(new SubmitRsvpRequest(GoingState)),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            // The server auto-waitlists a Going RSVP when the session is full, so both Going (a
            // spot opened up) and Waitlisted count as a successful join-waitlist intent.
            _joinWaitlistIdempotencyKeysBySessionId.TryRemove(sessionId, out _);
            return ClientCommandResult.Success;
        });

    private async Task<IReadOnlyList<SessionAdminResponse>> GetPublishedUpcomingAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("sessions", cancellationToken);
        response.EnsureSuccessStatusCode();
        var sessions = await response.Content.ReadFromJsonAsync<IReadOnlyList<SessionAdminResponse>>(
                cancellationToken: cancellationToken)
            ?? Array.Empty<SessionAdminResponse>();

        return sessions
            .Where(session =>
                string.Equals(session.Status, PublishedStatus, StringComparison.OrdinalIgnoreCase)
                || IsCanceled(session))
            .OrderBy(session => session.StartsAtUtc)
            .ToArray();
    }

    /// <summary>
    /// The "Submit your latest stats" card. The server picks the player's most recent played match
    /// still inside the submission window; 204 means there is nothing to report right now.
    /// </summary>
    private async Task<StatsPromptDto?> GetPendingStatsPromptAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("stats/submissions/pending", cancellationToken);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<StatsPromptDto>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // The prompt is a nicety - never fail the whole dashboard over it.
            return null;
        }
    }

    private async Task<RsvpResponseDto?> GetMyRsvpAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"sessions/{sessionId}/rsvp/me", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RsvpResponseDto>(
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<ClientCommandResult> ExecuteCommandAsync(
        Func<Task<ClientCommandResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
        catch (HttpRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.Message);
        }
    }

    private static bool IsClientError(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;

    private SessionSummaryDto ToSummary(
        SessionAdminResponse session,
        string statusLabel,
        string? relativeLabel)
    {
        var localStart = ToLocal(session.StartsAtUtc);
        return new SessionSummaryDto(
            session.SessionId,
            session.Title,
            Venue: string.Empty,
            session.Format,
            session.StartsAtUtc,
            DateLabel: localStart.ToString("MMM d", CultureInfo.InvariantCulture),
            TimeLabel: localStart.ToString("h:mm tt", CultureInfo.InvariantCulture),
            statusLabel,
            GoingCount: 0,
            session.Capacity,
            IsFull: false,
            WaitlistCount: 0,
            relativeLabel,
            IsCanceled(session),
            DeadlineLabel: BuildSummaryDeadlineLabel(session));
    }

    private string? BuildSummaryDeadlineLabel(SessionAdminResponse session)
    {
        if (IsCanceled(session))
        {
            return null;
        }

        if (timeProvider.GetUtcNow().UtcDateTime >= session.RsvpDeadlineUtc)
        {
            return "RSVP closed";
        }

        var localDeadline = ToLocal(session.RsvpDeadlineUtc);
        return $"RSVP closes {localDeadline.ToString("ddd h:mm tt", CultureInfo.InvariantCulture)}";
    }

    private static bool IsCanceled(SessionAdminResponse session) =>
        string.Equals(session.Status, CanceledStatus, StringComparison.OrdinalIgnoreCase);

    private string? BuildRelativeLabel(DateTime startsAtUtc)
    {
        var localToday = timeProvider.GetLocalNow().Date;
        var days = (ToLocal(startsAtUtc).Date - localToday).Days;
        return days switch
        {
            < 0 => null,
            0 => "Next match · today",
            1 => "Next match · tomorrow",
            _ => $"Next match · in {days} days",
        };
    }

    private static string BuildDeadlineLabel(DateTime deadlineUtc, DateTime nowUtc)
    {
        var remaining = deadlineUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return "RSVP closed";
        }

        return remaining.TotalDays >= 1
            ? $"closes {(int)remaining.TotalDays}d {remaining.Hours}h"
            : $"closes {remaining.Hours}h {remaining.Minutes}m";
    }

    private DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            // Backend timestamps arrive without a Z suffix (Kind=Unspecified) and are UTC by
            // convention; timestamps that already carry offset information must not be re-labeled.
            utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime(),
            timeProvider.LocalTimeZone);

    private static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");
}
