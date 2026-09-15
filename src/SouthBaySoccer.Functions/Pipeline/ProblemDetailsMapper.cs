using System.Net;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Application.Common;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ProblemDetailsMapper : IProblemDetailsMapper
{
    private const string ProblemBaseUri = "https://api.southbaysoccer.local/problems/";

    /// <summary>
    /// Absolute prefix for token outcomes. The MAUI client maps onboarding failures by this problem
    /// type (never by status alone), so it is a stable contract shared with the client.
    /// </summary>
    public const string OnboardingTokenProblemTypePrefix = "https://southbaysoccer/problems/onboarding-token-";

    public ProblemDetails Map(Exception exception, string correlationId)
    {
        var problem = exception switch
        {
            ValidationException fluentValidation => Create(
                HttpStatusCode.BadRequest,
                "Validation failed",
                "One or more request values are invalid.",
                "validation",
                fluentValidation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())),
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
            ApplicationUnauthenticatedException => Create(
                HttpStatusCode.Unauthorized,
                "Unauthorized",
                "Authentication is required.",
                "unauthorized"),
            PickupPalUserNotFoundException => Create(
                HttpStatusCode.NotFound,
                "Not found",
                "No Pickup Pal account was found for that phone number.",
                "pickup-pal-user-not-found"),
            // Stable onboarding problem types let the client tell a real token outcome apart from a
            // bare 404/409/403 elsewhere; details are fixed copy and never echo the token. The client
            // knows exactly four suffixes, so the email-taken case shares "already-registered".
            OnboardingTokenException onboarding => onboarding.Failure switch
            {
                OnboardingTokenFailure.Expired => Create(
                    HttpStatusCode.Gone,
                    "Link expired",
                    "The link has expired. Send the message again to get a new one.",
                    OnboardingTokenProblemTypePrefix + "expired"),
                OnboardingTokenFailure.AlreadyRegistered => Create(
                    HttpStatusCode.Conflict,
                    "Already registered",
                    "This phone number already has an account. Sign in instead.",
                    OnboardingTokenProblemTypePrefix + "already-registered"),
                OnboardingTokenFailure.EmailAlreadyRegistered => Create(
                    HttpStatusCode.Conflict,
                    "Email already registered",
                    "An account with this email already exists. Sign in instead.",
                    OnboardingTokenProblemTypePrefix + "already-registered"),
                OnboardingTokenFailure.Mismatch => Create(
                    HttpStatusCode.Forbidden,
                    "Sign-in could not be completed",
                    "The link does not match this sign-in. Start again from the sign-in screen.",
                    OnboardingTokenProblemTypePrefix + "mismatch"),
                _ => Create(
                    HttpStatusCode.NotFound,
                    "Link invalid",
                    "The link is invalid or was already used. Send the message again to get a new one.",
                    OnboardingTokenProblemTypePrefix + "invalid"),
            },
            ApplicationServiceUnavailableException unavailable => Create(
                HttpStatusCode.ServiceUnavailable,
                "Service unavailable",
                // Application-authored, user-safe message; nothing from the upstream response.
                unavailable.Message,
                "upstream-unavailable"),
            ApplicationNotFoundException => Create(
                HttpStatusCode.NotFound,
                "Not found",
                "The requested resource was not found.",
                "not-found"),
            ApplicationConflictException conflict => Create(
                HttpStatusCode.Conflict,
                "Conflict",
                // Application-authored, user-safe message (e.g. "No season covers the session start
                // date.") - pass it through instead of the generic detail so the client can surface
                // specifically what conflicted.
                conflict.Message,
                "conflict"),
            ApplicationPreconditionFailedException precondition => Create(
                HttpStatusCode.PreconditionFailed,
                "Draft changed",
                precondition.Message,
                "draft-revision-conflict"),
            ApplicationForbiddenException => Create(
                HttpStatusCode.Forbidden,
                "Forbidden",
                "You do not have permission to perform this action.",
                "forbidden"),
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
            // Onboarding token types are absolute (shared with the client); everything else is
            // relative to the API problem base.
            Type = type.StartsWith("https://", StringComparison.Ordinal) ? type : ProblemBaseUri + type,
        };

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }
}

