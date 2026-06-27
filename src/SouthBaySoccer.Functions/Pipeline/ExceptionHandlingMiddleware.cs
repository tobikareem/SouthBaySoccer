using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ExceptionHandlingMiddleware(
    ICorrelationContext correlationContext,
    IProblemDetailsMapper problemDetailsMapper,
    ILogger<ExceptionHandlingMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (context.IsHttpTrigger())
        {
            var correlationId = correlationContext.CorrelationId ?? Guid.NewGuid().ToString("N");
            var problem = problemDetailsMapper.Map(exception, correlationId);
            LogMappedException(exception, problem.Status, correlationId);

            var request = await context.GetHttpRequestDataAsync();
            if (request is null)
            {
                throw;
            }

            var response = request.CreateResponse((HttpStatusCode)(problem.Status ?? 500));
            response.Headers.TryAddWithoutValidation(CorrelationHeaderNames.CorrelationId, correlationId);
            await response.WriteAsJsonAsync(problem, cancellationToken: context.CancellationToken);
            context.GetInvocationResult().Value = response;
        }
    }

    private void LogMappedException(Exception exception, int? statusCode, string correlationId)
    {
        if (statusCode is >= 500)
        {
            logger.LogError(
                "HTTP request failed with status {StatusCode}. CorrelationId: {CorrelationId}. ExceptionType: {ExceptionType}",
                statusCode,
                correlationId,
                exception.GetType().Name);
            return;
        }

        logger.LogWarning(
            "HTTP request rejected with status {StatusCode}. CorrelationId: {CorrelationId}. ExceptionType: {ExceptionType}",
            statusCode,
            correlationId,
            exception.GetType().Name);
    }
}


