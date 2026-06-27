namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ValidationProblemException : ApiException
{
    public ValidationProblemException(IReadOnlyDictionary<string, string[]> errors)
        : base("The request is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
