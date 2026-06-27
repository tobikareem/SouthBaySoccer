namespace SouthBaySoccer.Functions.Pipeline;

public static class SouthBaySoccerMiddlewareOrder
{
    public static IReadOnlyList<Type> Default { get; } =
    [
        typeof(CorrelationMiddleware),
        typeof(ExceptionHandlingMiddleware),
        typeof(AuthenticationMiddleware),
        typeof(AuthorizationMiddleware),
    ];
}
