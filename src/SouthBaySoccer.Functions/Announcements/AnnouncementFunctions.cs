using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Announcements;

/// <summary>
/// HTTP endpoints for admin group broadcasts and the player announcement feed.
/// <para>
/// Every route is scoped to a single group and every handler proves the caller belongs to that
/// group, so the admin policy alone never grants cross-group reach: an admin can broadcast to their
/// own crew and to no one else.
/// </para>
/// </summary>
public sealed class AnnouncementFunctions(
    GetGroupAnnouncementsQueryHandler getGroupAnnouncementsHandler,
    GetUnreadAnnouncementCountQueryHandler getUnreadAnnouncementCountHandler,
    GetSentAnnouncementsQueryHandler getSentAnnouncementsHandler,
    PostAnnouncementCommandHandler postAnnouncementHandler,
    MarkGroupAnnouncementsReadCommandHandler markGroupAnnouncementsReadHandler,
    IdempotentRequestExecutor idempotentRequestExecutor)
{
    private const int DefaultFeedLimit = 20;
    private const int DefaultSentLimit = 10;

    [Function(nameof(GetGroupAnnouncements))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetGroupAnnouncements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "groups/{groupId:guid}/announcements")] HttpRequestData request,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(request.Url.Query);
        var result = await getGroupAnnouncementsHandler.HandleAsync(
            new GetGroupAnnouncementsQuery(
                groupId,
                ParseOptionalUtc(query["before"], "before"),
                ParseOptionalGuid(query["beforeId"], "beforeId"),
                ParseOptionalInt(query["limit"], "limit") ?? DefaultFeedLimit),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(MarkGroupAnnouncementsRead))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> MarkGroupAnnouncementsRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "groups/{groupId:guid}/announcements/read")] HttpRequestData request,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        // Naturally idempotent — the read mark only moves forward — so this deliberately skips the
        // idempotency executor rather than forcing the client to mint a key for a no-op-safe call.
        var result = await markGroupAnnouncementsReadHandler.HandleAsync(
            new MarkGroupAnnouncementsReadCommand(groupId),
            cancellationToken);

        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new MarkAnnouncementsReadResponse(result.GroupChatId, result.ReadThroughUtc, result.UnreadCount),
            cancellationToken);
    }

    [Function(nameof(GetUnreadAnnouncementCount))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetUnreadAnnouncementCount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/me/announcements/unread-count")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var unreadCount = await getUnreadAnnouncementCountHandler.HandleAsync(
            new GetUnreadAnnouncementCountQuery(),
            cancellationToken);

        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new UnreadAnnouncementsResponse(unreadCount),
            cancellationToken);
    }

    [Function(nameof(GetSentAnnouncements))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> GetSentAnnouncements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/me/announcements/sent")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(request.Url.Query);
        var result = await getSentAnnouncementsHandler.HandleAsync(
            new GetSentAnnouncementsQuery(ParseOptionalInt(query["limit"], "limit") ?? DefaultSentLimit),
            cancellationToken);

        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new SentAnnouncementsResponse(result.Select(ToDto).ToArray()),
            cancellationToken);
    }

    [Function(nameof(PostAnnouncement))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> PostAnnouncement(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "groups/{groupId:guid}/announcements")] HttpRequestData request,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<PostAnnouncementRequest>(request, cancellationToken);

        // A broadcast cannot be recalled, so a retried send must not post twice: the idempotency key
        // is what makes a timeout-and-retry safe for the admin.
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(PostAnnouncement),
            GetIdempotencyKey(request),
            new { groupId, body.Body, body.SendPush },
            async token =>
            {
                var result = await postAnnouncementHandler.HandleAsync(
                    new PostAnnouncementCommand(groupId, body.Body, body.SendPush),
                    token);
                return new IdempotentResponse<SentAnnouncementDto>(HttpStatusCode.Created, ToDto(result));
            },
            cancellationToken);
    }

    private static AnnouncementFeedResponse ToResponse(AnnouncementFeedResult result) =>
        new(
            result.GroupChatId,
            result.GroupName,
            result.Announcements.Select(ToDto).ToArray(),
            result.UnreadCount,
            result.NextCursorUtc,
            result.NextCursorId);

    private static AnnouncementDto ToDto(AnnouncementSummary announcement) =>
        new(
            announcement.Id,
            announcement.GroupChatId,
            announcement.GroupName,
            announcement.AuthorDisplayName,
            announcement.Body,
            announcement.SentAtUtc,
            announcement.IsUnread);

    private static SentAnnouncementDto ToDto(SentAnnouncementSummary announcement) =>
        new(
            announcement.Id,
            announcement.GroupChatId,
            announcement.GroupName,
            announcement.Body,
            announcement.SentAtUtc,
            announcement.ReadCount,
            announcement.RecipientCount);

    private static int? ParseOptionalInt(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                [parameterName] = [$"'{parameterName}' must be an integer."],
            });
        }

        return parsed;
    }

    private static Guid? ParseOptionalGuid(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed))
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                [parameterName] = [$"'{parameterName}' must be a GUID."],
            });
        }

        return parsed;
    }

    private static DateTime? ParseOptionalUtc(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                [parameterName] = [$"'{parameterName}' must be an ISO-8601 UTC timestamp."],
            });
        }

        return parsed;
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
