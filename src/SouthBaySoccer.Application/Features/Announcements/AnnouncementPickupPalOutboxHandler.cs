using SouthBaySoccer.Application.Features.Outbox;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Announcements;

/// <summary>Retries the external copy of an already-saved announcement without creating another local post.</summary>
public sealed class AnnouncementPickupPalOutboxHandler(
    IAnnouncementRepository announcements,
    IGroupChatRepository groups,
    IPickupPalAnnouncementClient client) : IOutboxMessageHandler
{
    public const string DeliveryRequested = "AnnouncementPickupPalDeliveryRequested";
    public string MessageType => DeliveryRequested;

    public async Task<OutboxHandlingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!OutboxPayload.TryReadGuid(message.PayloadJson, "AnnouncementId", out var announcementId)
            || announcementId == Guid.Empty)
        {
            return OutboxHandlingResult.Fail("InvalidPayload");
        }

        var announcement = await announcements.GetByIdAsync(announcementId, cancellationToken);
        if (announcement is null)
        {
            return OutboxHandlingResult.Fail("AnnouncementNotFound");
        }

        var group = await groups.GetByIdAsync(announcement.GroupChatId, cancellationToken);
        if (string.IsNullOrWhiteSpace(group?.ExternalId))
        {
            return OutboxHandlingResult.Fail("GroupDestinationNotFound");
        }

        // Pickup Pal has no documented deduplication contract. An ambiguous send can be retried;
        // this is at-least-once delivery, never a guarantee of exactly one external message.
        return ToResult(await client.SendAsync(group.ExternalId, announcement.Body, cancellationToken));
    }

    internal static OutboxHandlingResult ToResult(PickupPalAnnouncementSendResult result) => result switch
    {
        PickupPalAnnouncementSendResult.Sent => OutboxHandlingResult.Completed(),
        PickupPalAnnouncementSendResult.Rejected => OutboxHandlingResult.Fail("PickupPalRejected"),
        _ => OutboxHandlingResult.Retry("PickupPalUnavailable"),
    };
}
