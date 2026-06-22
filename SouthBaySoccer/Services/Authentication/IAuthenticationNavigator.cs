namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationNavigator
{
    Task ShowAuthenticatedAppAsync(CancellationToken cancellationToken = default);
}
