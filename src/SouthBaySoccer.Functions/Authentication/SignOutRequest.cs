namespace SouthBaySoccer.Functions.Authentication;

public sealed record SignOutRequest(string? RefreshToken)
{
    public static SignOutRequest Empty { get; } = new((string?)null);
}
