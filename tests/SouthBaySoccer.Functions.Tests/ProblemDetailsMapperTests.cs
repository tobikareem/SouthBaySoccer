using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class ProblemDetailsMapperTests
{
    private readonly ProblemDetailsMapper _mapper = new();

    public static TheoryData<Exception, int, string> StatusMappings => new()
    {
        { new ValidationProblemException(new Dictionary<string, string[]> { ["phoneNumber"] = ["Required"] }), 400, "Validation failed" },
        { new UnauthenticatedException(), 401, "Unauthorized" },
        { new ForbiddenException(), 403, "Forbidden" },
        { new ResourceNotFoundException(), 404, "Not found" },
        { new PickupPalUserNotFoundException(), 404, "Not found" },
        { new ConflictProblemException(), 409, "Conflict" },
        { new RateLimitExceededException(), 429, "Too many requests" },
        { new OnboardingTokenException(OnboardingTokenFailure.Expired), 410, "Link expired" },
        { new OnboardingTokenException(OnboardingTokenFailure.Invalid), 404, "Link invalid" },
        { new OnboardingTokenException(OnboardingTokenFailure.AlreadyRegistered), 409, "Already registered" },
        { new OnboardingTokenException(OnboardingTokenFailure.EmailAlreadyRegistered), 409, "Email already registered" },
        { new OnboardingTokenException(OnboardingTokenFailure.Mismatch), 403, "Sign-in could not be completed" },
        { new ApplicationServiceUnavailableException("Pickup Pal is unavailable right now. Try again later."), 503, "Service unavailable" },
        { new InvalidOperationException("sql timeout with private details"), 500, "Unexpected error" },
    };

    [Theory]
    [MemberData(nameof(StatusMappings))]
    public void Map_WhenExceptionIsKnownStatus_ReturnsProblemDetails(Exception exception, int statusCode, string title)
    {
        var problem = _mapper.Map(exception, "correlation-123");

        problem.Status.Should().Be(statusCode);
        problem.Title.Should().Be(title);
        problem.Type.Should().StartWith("https://");
        problem.Extensions["correlationId"].Should().Be("correlation-123");
    }

    [Fact]
    public void Map_WhenValidationException_ReturnsErrorsExtension()
    {
        var exception = new ValidationProblemException(new Dictionary<string, string[]>
        {
            ["phoneNumber"] = ["Required"],
        });

        var problem = _mapper.Map(exception, "correlation-123");

        problem.Extensions.Should().ContainKey("errors");
        problem.Extensions["errors"].Should().BeEquivalentTo(exception.Errors);
    }

    [Fact]
    public void Map_WhenApplicationConflictException_PassesThroughItsOwnMessageAsDetail()
    {
        // ApplicationConflictException messages are application-authored, user-safe strings (e.g. "No
        // season covers the session start date.") - the client should see the specific reason instead
        // of the generic "The request conflicts with the current state." detail.
        var exception = new ApplicationConflictException("No season covers the session start date.");

        var problem = _mapper.Map(exception, "correlation-123");

        problem.Status.Should().Be(409);
        problem.Title.Should().Be("Conflict");
        problem.Detail.Should().Be("No season covers the session start date.");
    }

    [Fact]
    public void Map_WhenDraftRevisionIsStale_ReturnsTypedPreconditionFailure()
    {
        var exception = new ApplicationPreconditionFailedException("The draft changed.");

        var problem = _mapper.Map(exception, "correlation-123");

        problem.Status.Should().Be(412);
        problem.Type.Should().EndWith("/draft-revision-conflict");
        problem.Detail.Should().Be("The draft changed.");
    }

    [Theory]
    [InlineData(OnboardingTokenFailure.Expired, "expired")]
    [InlineData(OnboardingTokenFailure.Invalid, "invalid")]
    [InlineData(OnboardingTokenFailure.AlreadyRegistered, "already-registered")]
    [InlineData(OnboardingTokenFailure.EmailAlreadyRegistered, "already-registered")]
    [InlineData(OnboardingTokenFailure.Mismatch, "mismatch")]
    public void Map_WhenOnboardingTokenFails_UsesStableProblemTypeWithFixedDetail(OnboardingTokenFailure failure, string suffix)
    {
        // The MAUI client (OnboardingClient.TokenProblemTypePrefix) maps token outcomes by this exact
        // type, never by status alone, and the detail must be fixed copy that never echoes the token.
        var problem = _mapper.Map(new OnboardingTokenException(failure), "correlation-123");

        problem.Type.Should().Be("https://southbaysoccer/problems/onboarding-token-" + suffix);
        problem.Detail.Should().NotBeNullOrWhiteSpace();
        problem.Detail.Should().NotContainEquivalentOf("token");
    }

    [Fact]
    public void Map_WhenUpstreamUnavailable_ReturnsServiceUnavailableWithApplicationMessage()
    {
        var problem = _mapper.Map(
            new ApplicationServiceUnavailableException("Pickup Pal is unavailable right now. Try again later."),
            "correlation-123");

        problem.Status.Should().Be(503);
        problem.Type.Should().EndWith("/upstream-unavailable");
        problem.Detail.Should().Be("Pickup Pal is unavailable right now. Try again later.");
    }

    [Fact]
    public void Map_WhenUnexpectedException_DoesNotExposeSensitiveMessage()
    {
        var exception = new InvalidOperationException("connection string secret=abc123");

        ProblemDetails problem = _mapper.Map(exception, "correlation-123");
        var extensionValues = problem.Extensions.Values.Select(value => value?.ToString() ?? string.Empty);

        problem.Detail.Should().Be("An unexpected error occurred.");
        problem.Detail.Should().NotContain("secret");
        extensionValues.Should().NotContain(value => value.Contains("abc123", StringComparison.OrdinalIgnoreCase));
    }
}
