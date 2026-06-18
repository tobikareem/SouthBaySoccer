namespace SouthBaySoccer.Services.Authentication;

public interface IAppStartupService
{
    Task TryRestoreSessionAsync(CancellationToken cancellationToken = default);
}
