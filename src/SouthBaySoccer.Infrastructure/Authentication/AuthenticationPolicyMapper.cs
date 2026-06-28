using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Infrastructure.Authentication;

public static class AuthenticationPolicyMapper
{
    public const string AuthenticatedPlayerPolicy = "AuthenticatedPlayer";

    public static IReadOnlyList<string> FromRoles(IReadOnlyList<string> roles)
    {
        var policies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AuthenticatedPlayerPolicy,
        };

        foreach (var role in roles)
        {
            if (Enum.TryParse<PlayerRole>(role, ignoreCase: true, out var playerRole))
            {
                AddRolePolicies(playerRole, policies);
            }
        }

        return policies.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddRolePolicies(PlayerRole role, ISet<string> policies)
    {
        if (role is PlayerRole.Owner or PlayerRole.Admin)
        {
            policies.Add("CanManagePlayers");
            policies.Add("CanManageSessions");
            policies.Add("CanCheckInPlayers");
            policies.Add("CanAssignTeams");
            policies.Add("CanRecordStats");
            policies.Add("CanViewFinancialStatus");
        }

        if (role is PlayerRole.GameAdmin)
        {
            policies.Add("CanManageSessions");
            policies.Add("CanCheckInPlayers");
            policies.Add("CanAssignTeams");
            policies.Add("CanRecordStats");
        }

        if (role is PlayerRole.Captain)
        {
            policies.Add("CanAssignTeams");
            policies.Add("CanRecordStats");
        }
    }
}

