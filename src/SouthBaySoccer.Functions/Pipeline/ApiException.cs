namespace SouthBaySoccer.Functions.Pipeline;

public abstract class ApiException : Exception
{
    protected ApiException(string message)
        : base(message)
    {
    }
}
