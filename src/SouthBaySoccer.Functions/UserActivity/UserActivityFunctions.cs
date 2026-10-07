using System.Globalization;
using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.UserActivity;
using SouthBaySoccer.Contracts.UserActivity;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.UserActivity;

public sealed class UserActivityFunctions(GetUserActivityQueryHandler handler)
{
    [Function(nameof(GetUserActivity))]
    [RequirePolicy(AuthenticationPolicies.IsSuperAdmin)]
    public async Task<HttpResponseData> GetUserActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/user-activity")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var parameters = HttpUtility.ParseQueryString(request.Url.Query);
        var query = new GetUserActivityQuery(Parse(parameters["page"], 1), Parse(parameters["pageSize"], 25));
        var result = await handler.HandleAsync(query, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(new UserActivityPageDto(
            result.Items.Select(row => new UserActivityEntryDto(row.Id, row.PlayerProfileId, row.DisplayName,
                row.ActivityType.ToString(), AsUtc(row.OccurredAtUtc), AsUtc(row.FirstRecordedActivityAtUtc),
                AsUtc(row.LastRecordedActivityAtUtc), row.SignInCount,
                row.Groups.Select(group => new UserActivityGroupDto(group.GroupName, group.Status)).ToArray())).ToArray(),
            query.Page, query.PageSize, result.HasMore, result.TrackingStartedAtUtc is { } started ? AsUtc(started) : null), cancellationToken: cancellationToken);
        return response;
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // Invalid numeric values flow into the same Application validation as out-of-range values.
    private static int Parse(string? value, int fallback) => value is null ? fallback
        : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
