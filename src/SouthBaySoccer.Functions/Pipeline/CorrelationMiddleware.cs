using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class CorrelationMiddleware(
    ICorrelationContext correlationContext,
    ICorrelationIdProvider correlationIdProvider) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!context.IsHttpTrigger())
        {
            await next(context);
            return;
        }

        var request = await context.GetHttpRequestDataAsync();
        IEnumerable<string>? candidateValues = null;
        request?.Headers.TryGetValues(CorrelationHeaderNames.CorrelationId, out candidateValues);
        var correlationId = correlationIdProvider.Resolve(candidateValues);
        correlationContext.CorrelationId = correlationId;

        await next(context);

        var response = context.GetHttpResponseData();
        response?.Headers.TryAddWithoutValidation(CorrelationHeaderNames.CorrelationId, correlationId);
    }
}
