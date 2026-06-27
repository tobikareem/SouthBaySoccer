namespace SouthBaySoccer.Functions.Authentication;

public sealed record SignOutCommand(Guid UserId, string? RefreshToken);
