using Microsoft.AspNetCore.Identity;

namespace SouthBaySoccer.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity user for SouthBaySoccer authentication.</summary>
public sealed class ApplicationIdentityUser : IdentityUser<Guid>
{
    /// <summary>Gets or sets the linked player profile id when the identity belongs to a player.</summary>
    public Guid? PlayerProfileId { get; set; }
}
