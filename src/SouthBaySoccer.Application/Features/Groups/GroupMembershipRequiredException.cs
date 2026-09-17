using SouthBaySoccer.Application.Common;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>
/// The caller tried a group-scoped action (RSVP, waitlist, self check-in, claim) on a session
/// whose group they are not an approved member of. Carries only the group's display name, which
/// is public within the app and safe to show.
/// </summary>
public sealed class GroupMembershipRequiredException : ApplicationExceptionBase
{
    public GroupMembershipRequiredException(string groupName)
        : base($"Only approved members of {groupName} can join this game.")
    {
        GroupName = groupName;
    }

    /// <summary>Gets the display name of the group the session belongs to.</summary>
    public string GroupName { get; }
}
