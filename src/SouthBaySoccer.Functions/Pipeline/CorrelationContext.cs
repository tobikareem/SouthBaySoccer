namespace SouthBaySoccer.Functions.Pipeline;

public sealed class CorrelationContext : ICorrelationContext
{
    public string? CorrelationId { get; set; }
}
