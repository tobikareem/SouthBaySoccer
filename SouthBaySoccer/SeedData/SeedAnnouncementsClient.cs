using System.Net;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedAnnouncementsClient(TimeProvider timeProvider) : IAnnouncementsClient
{
    private readonly object sync = new();
    private readonly Dictionary<(Guid GroupId, string Key), (PostAnnouncementRequest Request, SentAnnouncementDto Response)> posted = [];
    private static readonly IReadOnlyDictionary<Guid, (string Name, int MemberCount)> Groups =
        new Dictionary<Guid, (string Name, int MemberCount)>
        {
            [Guid.Parse("50000000-0000-0000-0000-000000000001")] = ("Bay Area Soccer", 349),
            [Guid.Parse("50000000-0000-0000-0000-000000000002")] = ("Morning Pick Up Soccer", 67)
        };
    private static readonly Guid PrimaryGroupId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly List<AnnouncementDto> announcements =
    [
        new(Guid.Parse("71000000-0000-0000-0000-000000000001"), PrimaryGroupId, "Bay Area Soccer",
            "Tobi", "Saturday pickup starts at 9:00 AM. Please arrive early for teams.", timeProvider.GetUtcNow().UtcDateTime.AddHours(-2), true),
        new(Guid.Parse("71000000-0000-0000-0000-000000000002"), PrimaryGroupId, "Bay Area Soccer",
            "Game Admin", "Bring both a green and a white shirt.", timeProvider.GetUtcNow().UtcDateTime.AddDays(-2), false)
    ];
    private readonly List<SentAnnouncementDto> sentAnnouncements =
    [
        new(
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            PrimaryGroupId,
            "Bay Area Soccer",
            "Saturday pickup starts at 9:00 AM. Please arrive early for teams.",
            timeProvider.GetUtcNow().UtcDateTime.AddHours(-2),
            349,
            349),
        new(
            Guid.Parse("72000000-0000-0000-0000-000000000002"),
            PrimaryGroupId,
            "Bay Area Soccer",
            "Bring both a green and a white shirt.",
            timeProvider.GetUtcNow().UtcDateTime.AddDays(-2),
            318,
            349)
    ];

    public Task<AnnouncementFeedResponse> GetFeedAsync(
        Guid groupId,
        int limit,
        DateTime? beforeUtc,
        Guid? beforeId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var items = announcements
                .Where(item => item.GroupId == groupId)
                .Where(item => beforeUtc is null
                    || item.SentAtUtc < beforeUtc
                    || (item.SentAtUtc == beforeUtc && beforeId is not null && item.Id.CompareTo(beforeId.Value) < 0))
                .OrderByDescending(item => item.SentAtUtc)
                .ThenByDescending(item => item.Id)
                .Take(limit + 1)
                .ToArray();
            var page = items.Take(limit).ToArray();
            var hasMore = items.Length > limit;
            var oldest = hasMore ? page[^1] : null;
            var groupName = Groups.TryGetValue(groupId, out var group) ? group.Name : string.Empty;
            return Task.FromResult(new AnnouncementFeedResponse(
                groupId,
                groupName,
                page,
                announcements.Count(item => item.GroupId == groupId && item.IsUnread),
                oldest?.SentAtUtc,
                oldest?.Id));
        }
    }

    public Task<MarkAnnouncementsReadResponse> MarkReadAsync(Guid groupId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            for (var index = 0; index < announcements.Count; index++)
            {
                if (announcements[index].GroupId == groupId)
                {
                    announcements[index] = announcements[index] with { IsUnread = false };
                }
            }
            return Task.FromResult(new MarkAnnouncementsReadResponse(groupId, timeProvider.GetUtcNow().UtcDateTime, 0));
        }
    }

    public Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(new UnreadAnnouncementsResponse(announcements.Count(item => item.IsUnread)));
        }
    }

    public Task<SentAnnouncementDto> PostAsync(Guid groupId, PostAnnouncementRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var mapKey = (groupId, idempotencyKey);
            if (posted.TryGetValue(mapKey, out var existing))
            {
                if (existing.Request == request)
                {
                    return Task.FromResult(existing.Response);
                }

                throw new ApiRequestException(
                    HttpStatusCode.Conflict,
                    "The idempotency key was already used for a different announcement.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var group = Groups.TryGetValue(groupId, out var knownGroup)
                ? knownGroup
                : (Name: string.Empty, MemberCount: 0);
            var dto = new AnnouncementDto(Guid.NewGuid(), groupId, group.Name, "Tobi", request.Body, now, false);
            announcements.Insert(0, dto);
            var sent = new SentAnnouncementDto(dto.Id, groupId, dto.GroupName, dto.Body, now, 0, group.MemberCount);
            posted[mapKey] = (request, sent);
            sentAnnouncements.Insert(0, sent);
            return Task.FromResult(sent);
        }
    }

    public Task<SentAnnouncementsResponse> GetSentAsync(int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(new SentAnnouncementsResponse(sentAnnouncements.Take(limit).ToArray()));
        }
    }
}
