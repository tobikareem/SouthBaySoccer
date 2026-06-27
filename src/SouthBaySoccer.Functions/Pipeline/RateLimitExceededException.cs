namespace SouthBaySoccer.Functions.Pipeline;

public sealed class RateLimitExceededException : ApiException
{
    public RateLimitExceededException()
        : base("Too many requests.")
    {
    }
}
