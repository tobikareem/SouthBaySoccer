namespace SouthBaySoccer.Functions.Pipeline;

public sealed record EndpointAccessRequirement(EndpointAccessKind Kind, string? Policy = null)
{
    public static EndpointAccessRequirement Anonymous { get; } = new(EndpointAccessKind.Anonymous);

    public static EndpointAccessRequirement RequirePolicy(string policy) =>
        new(EndpointAccessKind.Policy, policy);
}
