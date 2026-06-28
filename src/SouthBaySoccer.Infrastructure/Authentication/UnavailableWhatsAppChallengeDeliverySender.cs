namespace SouthBaySoccer.Infrastructure.Authentication;

public sealed class UnavailableWhatsAppChallengeDeliverySender : IWhatsAppChallengeDeliverySender
{
    public Task SendAsync(
        WhatsAppChallengeDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("WhatsApp challenge delivery provider is not configured.");
    }
}
