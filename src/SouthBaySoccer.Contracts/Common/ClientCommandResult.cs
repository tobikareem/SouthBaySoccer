namespace SouthBaySoccer.Contracts.Common;

public sealed record ClientCommandResult(
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ClientCommandResult Success { get; } = new(true);

    public static ClientCommandResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
