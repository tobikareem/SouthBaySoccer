namespace SouthBaySoccer.Services;

/// <summary>Role-name checks shared by page models; the server remains the enforcement point.</summary>
public static class PlayerRoles
{
    /// <summary>Whether the profile role is one of the administrative roles (Owner/Admin/GameAdmin).</summary>
    public static bool IsAdministrative(string? role) =>
        role is not null &&
        (role.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("GameAdmin", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("Game Admin", StringComparison.OrdinalIgnoreCase));
}
