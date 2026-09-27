namespace SouthBaySoccer.Functions.Authentication;

public static class AuthenticationPolicies
{
    public const string AuthenticatedPlayer = "AuthenticatedPlayer";
    public const string CanManagePlayers = "CanManagePlayers";
    public const string CanManageSessions = "CanManageSessions";
    public const string CanCheckInPlayers = "CanCheckInPlayers";
    public const string CanAssignTeams = "CanAssignTeams";
    public const string CanRecordStats = "CanRecordStats";

    /// <summary>The super admins (PlayerRole.Owner): appoint group admins and add players straight into groups.</summary>
    public const string IsSuperAdmin = "IsSuperAdmin";

    /// <summary>
    /// Global grant to manage group members (Owner). A group's own admins also manage its members,
    /// but that is per group and is evaluated in the handler, so endpoints open to group admins
    /// declare <see cref="AuthenticatedPlayer"/> and rely on the handler's check.
    /// </summary>
    public const string CanManageGroupMembers = "CanManageGroupMembers";
}
