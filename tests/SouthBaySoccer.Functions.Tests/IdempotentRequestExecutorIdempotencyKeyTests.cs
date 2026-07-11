using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Idempotency;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

/// <summary>
/// Validates <c>IdempotentRequestExecutor.ExecuteAsync</c> rejection of invalid Idempotency-Key values.
/// </summary>
/// <remarks>
/// In production, missing/blank keys are rejected by <c>SchedulingFunctions.GetIdempotencyKey</c> (private static,
/// requires live <c>HttpRequestData</c>, untestable here). The null/blank cases exercise a defensive branch in
/// <c>ExecuteAsync</c> unreachable in production via those endpoints; the &gt;160-character case is a faithful
/// behavioral proxy because <c>GetIdempotencyKey</c> does not enforce length limits. <c>GetIdempotencyKey</c>
/// itself remains untested—regressions in it would not be caught.
/// </remarks>
public sealed class IdempotentRequestExecutorIdempotencyKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WhenIdempotencyKeyIsMissingOrBlank_ThrowsValidationProblemForIdempotencyKeyHeader(
        string? idempotencyKey)
    {
        var executor = CreateExecutor();
        var operationInvoked = false;

        var act = () => executor.ExecuteAsync<object>(
            null!,
            "CreateSessionDraft",
            idempotencyKey!,
            new { },
            _ =>
            {
                operationInvoked = true;
                throw new InvalidOperationException("The operation must not run without a valid Idempotency-Key.");
            },
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ValidationProblemException>();
        thrown.Which.Errors.Should().ContainKey("Idempotency-Key");
        thrown.Which.Errors["Idempotency-Key"].Should().ContainSingle()
            .Which.Should().Be("A non-empty Idempotency-Key header of 160 characters or fewer is required.");
        operationInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdempotencyKeyExceedsMaximumLength_ThrowsValidationProblemForIdempotencyKeyHeader()
    {
        var executor = CreateExecutor();
        var oversizedKey = new string('a', 161);
        var operationInvoked = false;

        var act = () => executor.ExecuteAsync<object>(
            null!,
            "PublishSession",
            oversizedKey,
            new { },
            _ =>
            {
                operationInvoked = true;
                throw new InvalidOperationException("The operation must not run for an oversized Idempotency-Key.");
            },
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ValidationProblemException>();
        thrown.Which.Errors.Should().ContainKey("Idempotency-Key");
        operationInvoked.Should().BeFalse();
    }

    private static IdempotentRequestExecutor CreateExecutor() =>
        new(
            Mock.Of<ICurrentUser>(),
            Mock.Of<IClock>(),
            Mock.Of<IIdempotencyStore>());
}
