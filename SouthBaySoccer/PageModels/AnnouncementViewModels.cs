using CommunityToolkit.Mvvm.ComponentModel;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Contracts.Groups;

namespace SouthBaySoccer.PageModels;

public sealed partial class AnnouncementItemViewModel(AnnouncementDto announcement) : ObservableObject
{
    public Guid Id => announcement.Id;
    public Guid GroupId => announcement.GroupId;
    public string AuthorName => announcement.AuthorDisplayName;
    public string AuthorInitials => ToInitials(announcement.AuthorDisplayName);
    public string GroupName => announcement.GroupName;
    public string Body => announcement.Body;
    public DateTime SentAtUtc => announcement.SentAtUtc;

    /// <summary>
    /// First and last initials for the avatar — "Ayo Okafor" becomes "AO". Falls back to the
    /// leading character for a single-word name, and to nothing when the name is missing, so a
    /// soft-deleted author never renders a stray glyph.
    /// </summary>
    private static string ToInitials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant(),
        };
    }

    [ObservableProperty]
    private bool _isUnread = announcement.IsUnread;

    public string TimeLabel { get; init; } = string.Empty;
}

public sealed partial class GroupChoiceViewModel(GroupChatDto group) : ObservableObject
{
    public GroupChatDto Group => group;
    public Guid Id => group.Id;
    public string GroupName => group.GroupName;
    public int MemberCount => group.MemberCount;
    public string MemberCountLabel => $"{group.MemberCount} members";

    [ObservableProperty]
    private bool _isSelected;
}

public sealed class AnnouncementDayGroup(
    string name,
    IEnumerable<AnnouncementItemViewModel> items) : List<AnnouncementItemViewModel>(items)
{
    public string Name { get; } = name;
}
