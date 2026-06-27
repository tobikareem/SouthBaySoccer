using Microsoft.Azure.Functions.Worker;

namespace SouthBaySoccer.Functions.Authentication;

public static class FunctionContextCurrentUserExtensions
{
    private const string CurrentUserKey = "SouthBaySoccer.Functions.CurrentUser";

    public static FunctionCurrentUserPrincipal GetCurrentUser(this FunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(CurrentUserKey, out var value) &&
            value is FunctionCurrentUserPrincipal principal
                ? principal
                : FunctionCurrentUserPrincipal.Anonymous;
    }

    public static void SetCurrentUser(this FunctionContext context, FunctionCurrentUserPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);

        context.Items[CurrentUserKey] = principal;
    }
}
