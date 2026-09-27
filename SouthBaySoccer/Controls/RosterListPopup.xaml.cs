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
    public RosterListPopup(
        string title,
        IReadOnlyList<GameDayRosterItem> members,
        ICommand checkInCommand,
        ICommand linkCommand)
    {
        InitializeComponent();
        BindingContext = new RosterListContent(
            $"{title} · {members.Count}",
            members,
            "No one here yet.",
            checkInCommand,
            linkCommand);
    }
}

public sealed record RosterListContent(
    string Title,
    IReadOnlyList<GameDayRosterItem> Members,
    string EmptyMessage,
    ICommand CheckInCommand,
    ICommand LinkCommand)
{
    public bool HasMembers => Members.Count > 0;

    public bool IsEmpty => Members.Count == 0;

    /// <summary>
    /// Names the unmatched imports so the reason the list is longer than the actionable roster is
    /// stated rather than left for the reader to infer from greyed-out rows.
    /// </summary>
    public string UnlinkedSummary => Members.Count(member => member.IsUnlinked) switch
    {
        0 => string.Empty,
        1 => "1 person here hasn't been linked to a profile yet.",
        var count => $"{count} people here haven't been linked to a profile yet.",
    };

    public bool HasUnlinked => Members.Any(member => member.IsUnlinked);
}
