namespace SouthBaySoccer.Services;

public sealed class UserDialogService : IUserDialogService
{
    public async Task ShowAlertAsync(
        string title,
        string message,
        string cancel,
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.Windows.Count > 0)
        {
            var page = Application.Current.Windows[0].Page;
            if (page is not null)
            {
                await page.DisplayAlertAsync(title, message, cancel);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string accept,
        string cancel,
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.Windows.Count > 0)
        {
            var page = Application.Current.Windows[0].Page;
            if (page is not null)
            {
                var confirmed = await page.DisplayAlertAsync(title, message, accept, cancel);
                cancellationToken.ThrowIfCancellationRequested();
                return confirmed;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}
