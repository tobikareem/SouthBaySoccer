namespace SouthBaySoccer.Contracts.Authentication;

public sealed record VerifyWhatsAppChallengeRequest(string ChallengeToken, string CallbackUri);
