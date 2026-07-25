using System.Windows.Input;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.GameDay;

/// <summary>
/// <see cref="IRosterListPresenter"/> backed by a CommunityToolkit.Maui popup shown over the current
/// page. Marshals to the UI thread so it is safe to call straight from a page-model command.
/// </summary>
public sealed class PopupRosterListPresenter : IRosterListPresenter
{
    public Task ShowAsync(string title, IReadOnlyList<GameDayRosterItem> members, ICommand checkInCommand) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current?.CurrentPage is not { } page)
            {
                return;
            }

            await page.ShowPopupAsync(new RosterListPopup(title, members, checkInCommand));
        });
}
