using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>
/// Reads WhatsApp group-chat data from the Pickup Pal bot API. This boundary is <b>read-only</b>:
/// the API owner exposes only GET endpoints, so linkage is never written back to Pickup Pal — our
/// own database is the source of truth for which player belongs to which group. Reads are keyed on
/// the external Pickup Pal user id (the value stored on <c>PlayerProfile.PickupPalUserId</c>).
/// </summary>
public interface IPickupPalGroupClient
{
    /// <summary>Gets every group chat known to Pickup Pal, for the sign-in selection list.</summary>
    Task<IReadOnlyList<PickupPalGroupChat>> GetAllGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the group chats a Pickup Pal user is already linked to on the WhatsApp side. Used only to
    /// cross-check and seed our database; when Pickup Pal has no link, our database drives linkage.
    /// </summary>
    Task<IReadOnlyList<PickupPalGroupChat>> GetLinkedGroupsAsync(
        string pickupPalUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>One sanitized Pickup Pal group chat. Subscriber ids and other identifiers never cross this boundary.</summary>
public sealed record PickupPalGroupChat(
    string ExternalId,
    string GroupName,
    string? LinkageCode,
    string Status,
    int WhatsAppMemberCount,
    string? Timezone);
