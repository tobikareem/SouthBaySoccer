namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ConflictProblemException : ApiException
{
    public ConflictProblemException()
        : base("The request conflicts with the current state.")
    {
    }
}
