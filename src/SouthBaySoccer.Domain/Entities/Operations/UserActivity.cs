using System;
using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Operations;

/// <summary>A successful initial authentication session; never a token rotation or app opening.</summary>
public sealed class UserActivity : BaseEntity
{
    /// <summary>Gets or sets the authenticated identity reference.</summary>
    public Guid IdentityUserId { get; set; }
    /// <summary>Gets or sets the authenticated player reference.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the successful authentication kind.</summary>
    public UserActivityType ActivityType { get; set; }
    /// <summary>Gets or sets the UTC time of initial session issuance.</summary>
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>Gets or sets the unique session family reference, excluding token material.</summary>
    public Guid SessionFamilyId { get; set; }
}
