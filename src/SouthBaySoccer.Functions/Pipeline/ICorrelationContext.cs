namespace SouthBaySoccer.Functions.Pipeline;

public interface ICorrelationContext
{
    string? CorrelationId { get; set; }
}
