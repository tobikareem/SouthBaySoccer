namespace SouthBaySoccer.Contracts.Authentication;

public sealed record RequestWhatsAppChallengeRequest(string PhoneNumber, string CallbackUri);
