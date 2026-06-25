namespace SouthBaySoccer.Services.Clients;

public interface IProfileExternalLauncher
{
    Task<bool> OpenAccountAsync(CancellationToken cancellationToken);
}
