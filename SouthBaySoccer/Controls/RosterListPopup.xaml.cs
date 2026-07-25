using System.Windows.Input;
using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Controls;

/// <summary>
/// Bottom-sheet popup content listing the people in one Game Day category. Shown via
/// <c>Page.ShowPopupAsync</c> from the roster presenter; the title carries the count, and each row
/// exposes a Check in button (visible when the player can be checked in).
/// </summary>
public partial class RosterListPopup : ContentView
{
    public RosterListPopup(string title, IReadOnlyList<GameDayRosterItem> members, ICommand checkInCommand)
    {
        InitializeComponent();
        BindingContext = new RosterListContent(
            $"{title} · {members.Count}",
            members,
            "No one here yet.",
            checkInCommand);
    }
}

public sealed record RosterListContent(
    string Title,
    IReadOnlyList<GameDayRosterItem> Members,
    string EmptyMessage,
    ICommand CheckInCommand)
{
    public bool HasMembers => Members.Count > 0;

    public bool IsEmpty => Members.Count == 0;
}
