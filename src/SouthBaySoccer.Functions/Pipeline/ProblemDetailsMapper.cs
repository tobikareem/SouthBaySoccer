using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ProblemDetailsMapper : IProblemDetailsMapper
{
    private const string ProblemBaseUri = "https://api.southbaysoccer.local/problems/";

    public ProblemDetails Map(Exception exception, string correlationId)
    {
        var problem = exception switch
        {
            ValidationProblemException validation => Create(
                HttpStatusCode.BadRequest,
                "Validation failed",
                "One or more request values are invalid.",
                "validation",
                validation.Errors),
            UnauthenticatedException => Create(
                HttpStatusCode.Unauthorized,
                "Unauthorized",
                "Authentication is required.",
                "unauthorized"),
            UnauthorizedAccessException => Create(
                HttpStatusCode.Unauthorized,
                "Unauthorized",
                "Authentication is required.",
                "unauthorized"),
            ForbiddenException => Create(
                HttpStatusCode.Forbidden,
                "Forbidden",
                "You do not have permission to perform this action.",
                "forbidden"),
            ResourceNotFoundException => Create(
                HttpStatusCode.NotFound,
                "Not found",
                "The requested resource was not found.",
                "not-found"),
            ConflictProblemException => Create(
                HttpStatusCode.Conflict,
                "Conflict",
                "The request conflicts with the current state.",
                "conflict"),
            EndpointClassificationException => Create(
                HttpStatusCode.InternalServerError,
                "Endpoint configuration error",
                "The endpoint is not configured correctly.",
                "endpoint-configuration"),
            RateLimitExceededException => Create(
                (HttpStatusCode)429,
                "Too many requests",
                "Too many requests. Try again later.",
                "rate-limit"),
            _ => Create(
                HttpStatusCode.InternalServerError,
                "Unexpected error",
                "An unexpected error occurred.",
                "unexpected"),
        };

        problem.Extensions["correlationId"] = correlationId;
        return problem;
    }

    private static ProblemDetails Create(
        HttpStatusCode statusCode,
        string title,
        string detail,
        string type,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Type = ProblemBaseUri + type,
        };

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }
}
