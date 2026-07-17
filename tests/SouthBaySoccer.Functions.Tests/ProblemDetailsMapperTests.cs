using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
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
        { new InvalidOperationException("sql timeout with private details"), 500, "Unexpected error" },
    };

    [Theory]
    [MemberData(nameof(StatusMappings))]
    public void Map_WhenExceptionIsKnownStatus_ReturnsProblemDetails(Exception exception, int statusCode, string title)
    {
        var problem = _mapper.Map(exception, "correlation-123");

        problem.Status.Should().Be(statusCode);
        problem.Title.Should().Be(title);
        problem.Type.Should().StartWith("https://api.southbaysoccer.local/problems/");
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
