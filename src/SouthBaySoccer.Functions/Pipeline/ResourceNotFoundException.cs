namespace SouthBaySoccer.Functions.Pipeline;

public sealed class ResourceNotFoundException : ApiException
{
    public ResourceNotFoundException()
        : base("The requested resource was not found.")
    {
    }
}
