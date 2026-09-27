using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Controls;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Presentation helpers shared by the group membership screens (GRP-1). The server owns every
/// status; these only turn its strings into pills and captions.
/// </summary>
public static class GroupMembershipPresentation
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Approved = Success, Pending = Warning, Declined/Removed = Danger, anything else Neutral.</summary>
    public static BadgeVariant StatusVariant(string status) =>
        status switch
        {
            GroupMembershipStatuses.Approved => BadgeVariant.Success,
            GroupMembershipStatuses.Pending => BadgeVariant.Warning,
            GroupMembershipStatuses.Declined or GroupMembershipStatuses.Removed => BadgeVariant.Danger,
            _ => BadgeVariant.Neutral,
        };

    public static string MemberCountText(int memberCount) =>
        memberCount == 1 ? "1 member" : $"{memberCount.ToString(Culture)} members";

    public static string PendingCountText(int pendingCount) =>
        pendingCount == 1 ? "1 pending request" : $"{pendingCount.ToString(Culture)} pending requests";

    /// <summary>"Requested Jun 3" — the UTC timestamp shown as a month/day at the UI boundary.</summary>
    public static string RequestedOnText(DateTime requestedAtUtc) =>
        $"Requested {requestedAtUtc.ToLocalTime().ToString("MMM d", Culture)}";

    public static string SourceText(string source) =>
        source switch
        {
            GroupMembershipSources.WhatsApp => "via WhatsApp",
            GroupMembershipSources.SuperAdmin => "added by an owner",
            _ => "requested",
        };
}

/// <summary>A selectable group on the first-sign-in join step. Selection state mirrors the CollectionView's SelectedItems.</summary>
public sealed partial class GroupChoiceItem(GroupWithMembershipDto group) : ObservableObject
{
    public GroupWithMembershipDto Group { get; } = group;

    public Guid Id => Group.Id;

    public string Name => Group.GroupName;

    public string MemberCountText => GroupMembershipPresentation.MemberCountText(Group.MemberCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemanticDescription))]
    private bool _isSelected;

    public string SemanticDescription =>
        $"{Name}, {MemberCountText}, {(IsSelected ? "selected" : "not selected")}";
}

/// <summary>One row of the post-submit outcome: which groups were approved instantly and which wait.</summary>
public sealed record GroupRequestOutcome(Guid GroupChatId, string Name, string Status)
{
    public bool IsApproved => Status == GroupMembershipStatuses.Approved;

    public bool IsPending => !IsApproved;

    public BadgeVariant StatusVariant => GroupMembershipPresentation.StatusVariant(Status);

    public string Detail => IsApproved
        ? "Found in your WhatsApp groups."
        : "A group admin will review your request.";

    public string SemanticDescription => $"{Name}, {Status}. {Detail}";
}

/// <summary>A membership row on "My groups".</summary>
public sealed record MembershipRowItem(
    Guid GroupChatId,
    string Name,
    string Status,
    string Role,
    int? MemberCount,
    DateTime RequestedAtUtc)
{
    public bool IsApproved => Status == GroupMembershipStatuses.Approved;

    public bool IsPending => Status == GroupMembershipStatuses.Pending;

    public bool IsDeclined => Status == GroupMembershipStatuses.Declined;

    public bool IsAdmin => IsApproved && Role == GroupMemberRoles.Admin;

    /// <summary>Approved members leave; a pending request is withdrawn; a declined row can be re-requested.</summary>
    public bool CanLeave => IsApproved || IsPending;

    public string LeaveActionText => IsPending ? "Cancel" : "Leave";

    public string LeaveActionDescription => IsPending ? $"Cancel your request to join {Name}" : $"Leave {Name}";

    public string RequestAgainDescription => $"Request to join {Name} again";

    public BadgeVariant StatusVariant => GroupMembershipPresentation.StatusVariant(Status);

    /// <summary>"Admin · 349 members", "Requested Jun 3 · 67 members"; parts the source did not carry are left out.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(3);
            if (IsAdmin)
            {
                parts.Add("Admin");
            }

            if (IsPending && RequestedAtUtc != DateTime.MinValue)
            {
                parts.Add(GroupMembershipPresentation.RequestedOnText(RequestedAtUtc));
            }

            if (MemberCount is int count)
            {
                parts.Add(GroupMembershipPresentation.MemberCountText(count));
            }

            return string.Join(" · ", parts);
        }
    }

    public string SemanticDescription => Detail.Length == 0 ? $"{Name}, {Status}" : $"{Name}, {Status}. {Detail}";

    public static MembershipRowItem From(GroupWithMembershipDto group, GroupMembershipDto? membership) =>
        new(
            group.Id,
            group.GroupName,
            membership?.Status ?? group.MembershipStatus,
            membership?.Role ?? group.MemberRole,
            group.MemberCount,
            membership?.RequestedAtUtc ?? DateTime.MinValue);
}

/// <summary>A group the player is not in, offered with a Request button.</summary>
public sealed record JoinableGroupItem(Guid GroupChatId, string Name, int MemberCount)
{
    public string Detail => GroupMembershipPresentation.MemberCountText(MemberCount);

    public string SemanticDescription => $"Request to join {Name}, {Detail}";
}

/// <summary>A pending request or current member on the admin Members screen.</summary>
public sealed record GroupMemberItem(
    Guid PlayerProfileId,
    string Name,
    string Initials,
    string Status,
    string Role,
    string Source,
    DateTime RequestedAtUtc)
{
    public bool IsPending => Status == GroupMembershipStatuses.Pending;

    public bool IsAdmin => Role == GroupMemberRoles.Admin;

    public string Detail => IsPending
        ? GroupMembershipPresentation.RequestedOnText(RequestedAtUtc)
        : $"{(IsAdmin ? "Admin" : "Member")} · {GroupMembershipPresentation.SourceText(Source)}";

    public string SemanticDescription => $"{Name}, {Detail}";

    public string ApproveDescription => $"Approve {Name}";

    public string DeclineDescription => $"Decline {Name}";

    public string RemoveDescription => $"Remove {Name} from the group";

    public string AdminActionDescription => IsAdmin ? $"Remove {Name} as group admin" : $"Make {Name} a group admin";

    public static GroupMemberItem From(GroupMemberDto member) =>
        new(member.PlayerProfileId, member.DisplayName, member.Initials, member.Status, member.Role, member.Source, member.RequestedAtUtc);
}

/// <summary>A player search hit on the super-admin "Add member" field.</summary>
public sealed record PlayerSearchItem(Guid PlayerProfileId, string Name, string Initials, string? MaskedPhone)
{
    public string SemanticDescription => $"Add {Name} to the group";

    public static PlayerSearchItem From(PlayerSearchResultDto player) =>
        new(player.PlayerProfileId, player.DisplayName, player.Initials, player.MaskedPhone);
}

/// <summary>A group row on Profile ("Manage members") and on the super-admin group list.</summary>
public sealed record AdminGroupItem(Guid GroupChatId, string Name, string Detail, int PendingRequestCount)
{
    public bool HasPending => PendingRequestCount > 0;

    public string PendingCountText => PendingRequestCount.ToString(CultureInfo.InvariantCulture);

    public string PendingDescription => GroupMembershipPresentation.PendingCountText(PendingRequestCount);

    public string SemanticDescription => $"Manage members of {Name}. {Detail}";

    /// <summary>Catalogue rows carry live counts (super-admin list).</summary>
    public static AdminGroupItem FromCatalog(GroupWithMembershipDto group) =>
        new(
            group.Id,
            group.GroupName,
            group.PendingRequestCount > 0
                ? $"{GroupMembershipPresentation.MemberCountText(group.MemberCount)} · {GroupMembershipPresentation.PendingCountText(group.PendingRequestCount)}"
                : GroupMembershipPresentation.MemberCountText(group.MemberCount),
            group.PendingRequestCount);

    /// <summary>The player's own admin memberships (Profile "Manage members") have no counts; the Members screen shows them.</summary>
    public static AdminGroupItem FromMembership(GroupMembershipDto membership) =>
        new(membership.GroupChatId, membership.GroupName, "Approve requests · manage members", 0);
}
