using System.Windows.Input;
using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Shows a bottom-sheet popup listing the people in one Game Day category (Going, Waitlist, or
/// Checked in) when the player taps that count. Abstracted so the page model stays MAUI-free and the
/// popup plumbing is isolated to the view layer.
/// </summary>
public interface IRosterListPresenter
{
    /// <summary>
    /// Shows the category popup. <paramref name="checkInCommand"/> powers the per-row Check in
    /// button so an admin can check people in without leaving the popup;
    /// <paramref name="linkCommand"/> powers the per-row action on an unlinked imported name, which
    /// routes to the admin matching flow or the self-claim flow depending on the caller's role.
    /// </summary>
    Task ShowAsync(
        string title,
        IReadOnlyList<GameDayRosterItem> members,
        ICommand checkInCommand,
        ICommand linkCommand);
}
