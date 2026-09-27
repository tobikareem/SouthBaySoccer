using SouthBaySoccer.Contracts.Announcements;

namespace SouthBaySoccer.Services.Clients;

public interface IAnnouncementsClient
{
    Task<AnnouncementFeedResponse> GetFeedAsync(
        Guid groupId,
        int limit,
        DateTime? beforeUtc,
        Guid? beforeId,
        CancellationToken cancellationToken);

    Task<MarkAnnouncementsReadResponse> MarkReadAsync(
        Guid groupId,
        CancellationToken cancellationToken);

    Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken);

    Task<SentAnnouncementDto> PostAsync(
        Guid groupId,
        PostAnnouncementRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<SentAnnouncementsResponse> GetSentAsync(
        int limit,
        CancellationToken cancellationToken);
}
