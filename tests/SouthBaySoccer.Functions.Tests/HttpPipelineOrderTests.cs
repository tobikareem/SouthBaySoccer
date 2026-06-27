using FluentAssertions;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class HttpPipelineOrderTests
{
    [Fact]
    public void Default_WhenRegistered_UsesSpecOrder()
    {
        SouthBaySoccerMiddlewareOrder.Default.Should().Equal(
            typeof(CorrelationMiddleware),
            typeof(ExceptionHandlingMiddleware),
            typeof(AuthenticationMiddleware),
            typeof(AuthorizationMiddleware));
    }
}

