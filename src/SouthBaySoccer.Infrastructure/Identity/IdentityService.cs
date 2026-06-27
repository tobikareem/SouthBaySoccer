using Microsoft.AspNetCore.Identity;
using SouthBaySoccer.Application.Abstractions.Authentication;

namespace SouthBaySoccer.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity-backed implementation of application identity operations.
/// </summary>
public sealed class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationIdentityUser> userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityService"/> class.
    /// </summary>
    /// <param name="userManager">The ASP.NET Core Identity user manager.</param>
    public IdentityService(UserManager<ApplicationIdentityUser> userManager)
    {
        this.userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        return user is not null && await userManager.CheckPasswordAsync(user, password);
    }
}
