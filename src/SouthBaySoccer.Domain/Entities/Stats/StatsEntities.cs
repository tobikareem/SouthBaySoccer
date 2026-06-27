using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Stats;

/// <summary>Represents a played match within a session.</summary>
public class Match : BaseEntity { public Guid SessionId { get; set; } public int MatchNumber { get; set; } public MatchStatus Status { get; set; } public DateTime? StartedAtUtc { get; set; } public DateTime? CompletedAtUtc { get; set; } }
/// <summary>Represents a team within a single match.</summary>
public class MatchTeam : BaseEntity { public Guid MatchId { get; set; } public int TeamNumber { get; set; } public string Name { get; set; } = string.Empty; public Guid? CaptainPlayerProfileId { get; set; } }
/// <summary>Represents a player assignment to a match team.</summary>
public class TeamAssignment : BaseEntity { public Guid MatchId { get; set; } public Guid MatchTeamId { get; set; } public Guid PlayerProfileId { get; set; } }
/// <summary>Represents a team result for a match.</summary>
public class MatchResult : BaseEntity { public Guid MatchId { get; set; } public Guid MatchTeamId { get; set; } public int Wins { get; set; } public int Draws { get; set; } public int Losses { get; set; } public int GoalsFor { get; set; } public int GoalsAgainst { get; set; } }
/// <summary>Represents participation facts for a player in a match.</summary>
public class PlayerMatchStats : BaseEntity { public Guid MatchId { get; set; } public Guid PlayerProfileId { get; set; } public bool Played { get; set; } public int? MinutesPlayed { get; set; } public bool Started { get; set; } public bool PlayedGoalkeeper { get; set; } public string? Position { get; set; } }
/// <summary>Represents a raw match event such as a goal or card.</summary>
public class MatchEvent : BaseEntity { public Guid MatchId { get; set; } public Guid? PlayerProfileId { get; set; } public Guid? AssistPlayerProfileId { get; set; } public Guid? MatchTeamId { get; set; } public MatchEventType EventType { get; set; } public int Minute { get; set; } }
/// <summary>Represents a peer rating vote.</summary>
public class PlayerRatingVote : BaseEntity { public Guid MatchId { get; set; } public Guid VoterPlayerProfileId { get; set; } public Guid RatedPlayerProfileId { get; set; } public int Score { get; set; } }
/// <summary>Represents a player like given after a match.</summary>
public class PlayerLike : BaseEntity { public Guid MatchId { get; set; } public Guid GiverPlayerProfileId { get; set; } public Guid ReceiverPlayerProfileId { get; set; } }
/// <summary>Represents an explicit match award such as MVP.</summary>
public class MatchAward : BaseEntity { public Guid MatchId { get; set; } public Guid PlayerProfileId { get; set; } public Guid? AwardedByPlayerProfileId { get; set; } public MatchAwardType AwardType { get; set; } }
/// <summary>Represents an auditable correction after stats are locked or published.</summary>
public class StatCorrection : BaseEntity { public Guid MatchId { get; set; } public Guid? PlayerProfileId { get; set; } public Guid CorrectedByPlayerProfileId { get; set; } public string Reason { get; set; } = string.Empty; public string BeforeJson { get; set; } = string.Empty; public string AfterJson { get; set; } = string.Empty; public DateTime CorrectedAtUtc { get; set; } }
