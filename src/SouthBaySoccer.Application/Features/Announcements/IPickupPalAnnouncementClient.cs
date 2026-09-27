using System.Threading;
using System.Threading.Tasks;

namespace SouthBaySoccer.Application.Features.Announcements;

/// <summary>Sends a group announcement to Pickup Pal.</summary>
public interface IPickupPalAnnouncementClient
{
    /// <summary>Sends the message to the group's WhatsApp chat.</summary>
    Task<PickupPalAnnouncementSendResult> SendAsync(
        string chatId,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes whether an announcement was accepted or requires another attempt.</summary>
public enum PickupPalAnnouncementSendResult
{
    /// <summary>Pickup Pal accepted the message.</summary>
    Sent,
    /// <summary>A temporary failure allows a later retry.</summary>
    Retry,
    /// <summary>Pickup Pal rejected the request permanently.</summary>
    Rejected
}
