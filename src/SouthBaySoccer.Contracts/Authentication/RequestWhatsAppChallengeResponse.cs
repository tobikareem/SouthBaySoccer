namespace SouthBaySoccer.Contracts.Authentication;

public sealed record RequestWhatsAppChallengeResponse(string ChallengeId, DateTime ExpiresAtUtc);
