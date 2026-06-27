using SouthBaySoccer.Application.Abstractions.Authentication;

namespace SouthBaySoccer.Functions.Authentication;

public sealed class FunctionCurrentUser : ICurrentUser, IFunctionCurrentUserAccessor
{
    private FunctionCurrentUserPrincipal principal = FunctionCurrentUserPrincipal.Anonymous;

    public Guid? UserId => principal.UserId;

    public bool IsAuthenticated => principal.IsAuthenticated;

    public bool IsInRole(string role) => principal.IsInRole(role);

    public bool HasPolicy(string policy) => principal.HasPolicy(policy);

    public void SetAnonymous() => principal = FunctionCurrentUserPrincipal.Anonymous;

    public void SetCurrentUser(FunctionCurrentUserPrincipal currentUser) =>
        principal = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
}
