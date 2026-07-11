using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Idempotency;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

/// <summary>
/// Covers the missing-<c>Idempotency-Key</c> rejection shared by <c>SchedulingFunctions.CreateSessionDraft</c>,
/// <c>UpdateSession</c>, and <c>PublishSession</c> (see <c>SchedulingFunctions.GetIdempotencyKey</c>).
/// </summary>
/// <remarks>
/// <see cref="SchedulingFunctions.GetIdempotencyKey"/> is a private static helper that reads the header
/// straight off an <see cref="Microsoft.Azure.Functions.Worker.Http.HttpRequestData"/>; that type is
/// abstract and requires a live <see cref="Microsoft.Azure.Functions.Worker.FunctionContext"/> to
/// construct, and no test double for it exists anywhere in this project (every other test here is either
/// reflection-based endpoint metadata or a workflow-level unit test that never touches
/// <c>HttpRequestData</c>). Building one bespoke to this test would introduce a testing pattern not used
/// elsewhere in Functions.Tests. All three endpoints route a missing/blank key through the identical
/// rejection here: <see cref="IdempotentRequestExecutor.ExecuteAsync{TResponse}"/> independently validates
/// a non-empty, &lt;=160-character key and throws the same <see cref="ValidationProblemException"/> (RFC
/// 7807-mapped by the exception-handling middleware) that <c>GetIdempotencyKey</c> throws when the header
/// itself is absent — this is the closest reachable seam, and the assertions below prove the operation
/// never runs for an invalid key.
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
