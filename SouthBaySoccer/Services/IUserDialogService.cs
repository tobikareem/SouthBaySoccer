namespace SouthBaySoccer.Services;

public interface IUserDialogService
{
    Task ShowAlertAsync(
        string title,
        string message,
        string cancel,
        CancellationToken cancellationToken = default);
}
