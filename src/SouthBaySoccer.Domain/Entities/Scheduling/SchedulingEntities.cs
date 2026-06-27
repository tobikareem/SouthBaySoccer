using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Scheduling;

/// <summary>Represents an explicit soccer season.</summary>
public class Season : BaseEntity { public string Name { get; set; } = string.Empty; public DateTime StartsAtUtc { get; set; } public DateTime EndsAtUtc { get; set; } }
/// <summary>Represents a playing venue.</summary>
public class Venue : BaseEntity { public string Name { get; set; } = string.Empty; public string Locality { get; set; } = string.Empty; public string? Address { get; set; } public string? MapsProviderReference { get; set; } }
/// <summary>Represents a recurrence rule for generating sessions.</summary>
public class RecurrenceRule : BaseEntity { public string Name { get; set; } = string.Empty; public string TimeZoneId { get; set; } = string.Empty; public string Rule { get; set; } = string.Empty; }
/// <summary>Represents a scheduled pickup soccer session.</summary>
public class Session : BaseEntity
{
    public Guid SeasonId { get; set; }
    public Guid VenueId { get; set; }
    public Guid? RecurrenceRuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int TeamCount { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime CheckInOpensAtUtc { get; set; }
    public DateTime CheckInClosesAtUtc { get; set; }
    public DateTime RsvpDeadlineUtc { get; set; }
    public string? OccurrenceKey { get; set; }
    public SessionStatus Status { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
/// <summary>Represents explicit attendance intent for a session.</summary>
public class RsvpResponse : BaseEntity { public Guid SessionId { get; set; } public Guid PlayerProfileId { get; set; } public RsvpStatus Status { get; set; } public byte[] RowVersion { get; set; } = Array.Empty<byte>(); }
/// <summary>Represents ordered waitlist state for a full session.</summary>
public class WaitlistEntry : BaseEntity { public Guid SessionId { get; set; } public Guid PlayerProfileId { get; set; } public int Position { get; set; } public WaitlistEntryStatus Status { get; set; } }
/// <summary>Represents actual session check-in.</summary>
public class CheckIn : BaseEntity { public Guid SessionId { get; set; } public Guid PlayerProfileId { get; set; } public Guid? CheckedInByPlayerProfileId { get; set; } public DateTime CheckedInAtUtc { get; set; } public AttendanceOutcome Outcome { get; set; } }
/// <summary>Represents an audited admin override.</summary>
public class AdminOverride : BaseEntity { public Guid SessionId { get; set; } public Guid PlayerProfileId { get; set; } public Guid AdminPlayerProfileId { get; set; } public AdminOverrideType OverrideType { get; set; } public string Reason { get; set; } = string.Empty; public DateTime AppliedAtUtc { get; set; } }
