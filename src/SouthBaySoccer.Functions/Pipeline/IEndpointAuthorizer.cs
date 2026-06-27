namespace SouthBaySoccer.Functions.Pipeline;

public interface IEndpointAuthorizer
{
    void Authorize(string entryPoint);
}
