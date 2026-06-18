namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationCoordinator
{
    Task<bool> HandleCallbackAsync(Uri callbackUri, CancellationToken cancellationToken = default);
}
