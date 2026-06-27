using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class AuthorizationMiddleware(IEndpointAuthorizer endpointAuthorizer) : IFunctionsWorkerMiddleware
{
    public Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!context.IsHttpTrigger())
        {
            return next(context);
        }

        endpointAuthorizer.Authorize(context.FunctionDefinition.EntryPoint);
        return next(context);
    }
}
