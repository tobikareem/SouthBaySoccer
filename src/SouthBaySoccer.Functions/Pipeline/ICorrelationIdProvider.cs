namespace SouthBaySoccer.Functions.Pipeline;

public interface ICorrelationIdProvider
{
    string Resolve(IEnumerable<string>? candidateValues);
}
