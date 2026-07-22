namespace SouthBaySoccer.Services;

public interface IUserDialogService
{
    Task ShowAlertAsync(
        string title,
        string message,
        string cancel,
        CancellationToken cancellationToken = default);

    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string accept,
        string cancel,
        CancellationToken cancellationToken = default);
}
