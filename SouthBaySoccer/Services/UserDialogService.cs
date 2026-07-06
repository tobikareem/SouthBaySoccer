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
}

