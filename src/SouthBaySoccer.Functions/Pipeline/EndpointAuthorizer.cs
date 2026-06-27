using SouthBaySoccer.Application.Abstractions.Authentication;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class EndpointAuthorizer(
    IEndpointPolicyResolver endpointPolicyResolver,
    ICurrentUser currentUser) : IEndpointAuthorizer
{
    public void Authorize(string entryPoint)
    {
        var requirement = endpointPolicyResolver.Resolve(entryPoint);
        if (requirement.Kind == EndpointAccessKind.Anonymous)
        {
            return;
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }

        if (string.IsNullOrWhiteSpace(requirement.Policy) || !currentUser.HasPolicy(requirement.Policy))
        {
            throw new ForbiddenException();
        }
    }
}

