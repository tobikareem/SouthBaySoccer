namespace SouthBaySoccer.Functions.Pipeline;

public interface IEndpointPolicyResolver
{
    EndpointAccessRequirement Resolve(string entryPoint);
}
