namespace SouthBaySoccer.Infrastructure.Authentication;

public interface IWhatsAppChallengeDeliverySender
{
    Task SendAsync(
        WhatsAppChallengeDeliveryRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WhatsAppChallengeDeliveryRequest(
    string PhoneNumber,
    string MaskedPhoneNumber,
    string ChallengeToken,
    string CallbackUri,
    DateTime ExpiresAtUtc);
