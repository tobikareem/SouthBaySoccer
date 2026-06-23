namespace SouthBaySoccer.Services.Authentication;

public interface IExternalLauncher
{
    Task<bool> OpenPickupPalBotAsync(CancellationToken cancellationToken);
    Task<bool> OpenPickupPalSignupAsync(CancellationToken cancellationToken);
}
