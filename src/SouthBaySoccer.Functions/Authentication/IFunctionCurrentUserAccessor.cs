namespace SouthBaySoccer.Functions.Authentication;

public interface IFunctionCurrentUserAccessor
{
    void SetAnonymous();

    void SetCurrentUser(FunctionCurrentUserPrincipal currentUser);
}
