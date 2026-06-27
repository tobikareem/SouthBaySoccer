namespace SouthBaySoccer.Functions.Pipeline;

public sealed class UnauthenticatedException : ApiException
{
    public UnauthenticatedException()
        : base("Authentication is required.")
    {
    }
}
