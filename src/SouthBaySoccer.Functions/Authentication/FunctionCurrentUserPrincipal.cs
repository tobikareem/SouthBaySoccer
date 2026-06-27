namespace SouthBaySoccer.Functions.Authentication;

public sealed record FunctionCurrentUserPrincipal(
    Guid? UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Policies)
{
    public static FunctionCurrentUserPrincipal Anonymous { get; } =
        new(null, Array.Empty<string>(), Array.Empty<string>());

    public bool IsAuthenticated => UserId is not null;

    public static FunctionCurrentUserPrincipal Authenticated(
        Guid userId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> policies) =>
        new(userId, roles, policies);

    public bool IsInRole(string role) =>
        Roles.Any(candidate => string.Equals(candidate, role, StringComparison.OrdinalIgnoreCase));

    public bool HasPolicy(string policy) =>
        Policies.Any(candidate => string.Equals(candidate, policy, StringComparison.OrdinalIgnoreCase));
}
