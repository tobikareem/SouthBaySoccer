namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ForbiddenException : ApiException
{
    public ForbiddenException()
        : base("You do not have permission to perform this action.")
    {
    }
}
