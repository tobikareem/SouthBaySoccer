using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Functions.Authentication;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class AuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!context.IsHttpTrigger())
        {
            await next(context);
            return;
        }

        var currentUserAccessor = context.InstanceServices.GetService<IFunctionCurrentUserAccessor>();
        currentUserAccessor?.SetAnonymous();
        context.SetCurrentUser(FunctionCurrentUserPrincipal.Anonymous);

        var request = await context.GetHttpRequestDataAsync();
        if (request is null)
        {
            await next(context);
            return;
        }

        var bearerToken = BearerTokenReader.TryRead(request.Headers);
        if (bearerToken is null)
        {
            await next(context);
            return;
        }

        var tokenService = context.InstanceServices.GetService<ITokenService>();
        if (tokenService is null)
        {
            throw new UnauthenticatedException();
        }

        var validation = tokenService.ValidateAccessToken(bearerToken);
        if (!validation.IsValid || validation.UserId is null)
        {
            throw new UnauthenticatedException();
        }

        var principal = FunctionCurrentUserPrincipal.Authenticated(
            validation.UserId.Value,
            validation.Roles,
            validation.Policies);

        currentUserAccessor?.SetCurrentUser(principal);
        context.SetCurrentUser(principal);

        await next(context);
    }
}
