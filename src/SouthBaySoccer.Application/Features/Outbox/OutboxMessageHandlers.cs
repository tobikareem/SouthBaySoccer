using System.Text.Json;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Operations;

namespace SouthBaySoccer.Application.Features.Outbox;

/// <summary>
/// Retries an RSVP roster push. The payload only identifies the player and session; the desired
/// roster state is re-derived from the current local RSVP so the last local write wins regardless
/// of the order rows are processed in.
/// </summary>
public sealed class RsvpPickupPalSyncOutboxHandler(IRsvpPickupPalSyncService syncService) : IOutboxMessageHandler
{
    /// <summary>Reason code for a payload the handler cannot read.</summary>
    public const string InvalidPayloadCode = "InvalidPayload";

    public string MessageType => RsvpOutboxMessages.RsvpPickupPalSyncRequested;

    public async Task<OutboxHandlingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!OutboxPayload.TryReadGuid(message.PayloadJson, "SessionId", out var sessionId)
            || !OutboxPayload.TryReadGuid(message.PayloadJson, "PlayerProfileId", out var playerProfileId))
        {
            return OutboxHandlingResult.Fail(InvalidPayloadCode);
        }

        var outcome = await syncService.PushCurrentStateAsync(sessionId, playerProfileId, cancellationToken);
        return outcome.IsRetryable
            ? OutboxHandlingResult.Retry(outcome.ErrorCode ?? PickupPalSyncErrorCodes.Unexpected)
            : OutboxHandlingResult.Completed();
    }
}

/// <summary>
/// Retries a session game push (create, update, or terminate). The payload identifies the session
/// and the admin who acted; the action is re-derived from the session's current local state so the
/// last local write wins. Terminal outcomes (rejected, missing creator or group) complete the row:
/// they are recorded on the session and only a new admin write reopens them.
/// </summary>
public sealed class SessionPickupPalSyncOutboxHandler(ISessionPickupPalSyncService syncService) : IOutboxMessageHandler
{
    /// <summary>Reason code for a payload the handler cannot read.</summary>
    public const string InvalidPayloadCode = "InvalidPayload";

    public string MessageType => SessionOutboxMessages.SessionPickupPalSyncRequested;

    public async Task<OutboxHandlingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!OutboxPayload.TryReadGuid(message.PayloadJson, "SessionId", out var sessionId))
        {
            return OutboxHandlingResult.Fail(InvalidPayloadCode);
        }

        Guid? actingPlayerProfileId = OutboxPayload.TryReadGuid(message.PayloadJson, "ActingPlayerProfileId", out var actingId)
            ? actingId
            : null;

        var outcome = await syncService.PushCurrentStateAsync(sessionId, actingPlayerProfileId, cancellationToken);
        return outcome.IsRetryable
            ? OutboxHandlingResult.Retry(outcome.ErrorCode ?? SessionPickupPalSyncErrorCodes.Unexpected)
            : OutboxHandlingResult.Completed();
    }
}

/// <summary>
/// Retries a Pickup Pal account deletion the player explicitly asked for. A user Pickup Pal no
/// longer has counts as deleted (the client maps 404 to success).
/// </summary>
public sealed class PickupPalUserDeletionOutboxHandler(IPickupPalOnboardingClient onboardingClient) : IOutboxMessageHandler
{
    /// <summary>Reason code for a payload the handler cannot read.</summary>
    public const string InvalidPayloadCode = "InvalidPayload";

    public string MessageType => OnboardingOutboxMessages.PickupPalUserDeletionRequested;

    public async Task<OutboxHandlingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!OutboxPayload.TryReadString(message.PayloadJson, "PickupPalUserId", out var pickupPalUserId))
        {
            return OutboxHandlingResult.Fail(InvalidPayloadCode);
        }

        try
        {
            await onboardingClient.DeleteUserAsync(pickupPalUserId, cancellationToken);
            return OutboxHandlingResult.Completed();
        }
        catch (ApplicationServiceUnavailableException)
        {
            return OutboxHandlingResult.Retry(PickupPalSyncErrorCodes.Unavailable);
        }
    }
}

/// <summary>Minimal, allocation-light readers for the anonymous-object payloads the handlers write.</summary>
internal static class OutboxPayload
{
    public static bool TryReadGuid(string payloadJson, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return TryReadString(payloadJson, propertyName, out var text) && Guid.TryParse(text, out value);
    }

    public static bool TryReadString(string payloadJson, string propertyName, out string value)
    {
        value = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = property.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            value = text;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
