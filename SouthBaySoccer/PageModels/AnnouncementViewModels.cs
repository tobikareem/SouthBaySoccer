using CommunityToolkit.Mvvm.ComponentModel;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Contracts.Groups;

namespace SouthBaySoccer.PageModels;

public sealed partial class AnnouncementItemViewModel(AnnouncementDto announcement) : ObservableObject
{
    public Guid Id => announcement.Id;
    public Guid GroupId => announcement.GroupId;
    public string AuthorName => announcement.AuthorDisplayName;
    public string GroupName => announcement.GroupName;
    public string Body => announcement.Body;
    public DateTime SentAtUtc => announcement.SentAtUtc;

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
