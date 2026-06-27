using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Operations;

/// <summary>Represents a durable transactional outbox message.</summary>
public class OutboxMessage : BaseEntity { public string MessageType { get; set; } = string.Empty; public string PayloadJson { get; set; } = string.Empty; public OutboxMessageStatus Status { get; set; } public DateTime AvailableAtUtc { get; set; } public int AttemptCount { get; set; } public string? LockToken { get; set; } public DateTime? LockedUntilUtc { get; set; } public DateTime? ProcessedAtUtc { get; set; } public string? DeadLetterReason { get; set; } public string? CorrelationId { get; set; } public string? IdempotencyKey { get; set; } }
/// <summary>Represents a logical notification message.</summary>
public class NotificationMessage : BaseEntity { public Guid? OutboxMessageId { get; set; } public Guid? AlertInstanceId { get; set; } public Guid? SessionId { get; set; } public string TemplateKey { get; set; } = string.Empty; public NotificationChannel Channel { get; set; } public string? Subject { get; set; } public string PayloadJson { get; set; } = string.Empty; public NotificationStatus Status { get; set; } public DateTime? ScheduledForUtc { get; set; } public int Priority { get; set; } public string? IdempotencyKey { get; set; } }
/// <summary>Represents a snapshotted notification recipient.</summary>
public class NotificationRecipient : BaseEntity { public Guid NotificationMessageId { get; set; } public Guid? PlayerProfileId { get; set; } public NotificationChannel Channel { get; set; } public string DestinationHash { get; set; } = string.Empty; public string MaskedDestination { get; set; } = string.Empty; }
/// <summary>Represents a provider notification delivery attempt.</summary>
public class NotificationDelivery : BaseEntity { public Guid NotificationRecipientId { get; set; } public int AttemptNumber { get; set; } public NotificationDeliveryStatus Status { get; set; } public string? ProviderMessageId { get; set; } public string? FailureCategory { get; set; } public DateTime? NextRetryAtUtc { get; set; } public DateTime? DeliveredAtUtc { get; set; } public DateTime? FailedAtUtc { get; set; } }
/// <summary>Represents an alert rule definition.</summary>
public class AlertRule : BaseEntity { public string Name { get; set; } = string.Empty; public AlertSeverity Severity { get; set; } public bool IsEnabled { get; set; } = true; public TimeSpan? SuppressionWindow { get; set; } public DateTime? LastFiredAtUtc { get; set; } }
/// <summary>Represents a raised alert instance.</summary>
public class AlertInstance : BaseEntity { public Guid AlertRuleId { get; set; } public string DeduplicationKey { get; set; } = string.Empty; public AlertStatus Status { get; set; } public Guid? SessionId { get; set; } public Guid? PlayerProfileId { get; set; } public string? ContextJson { get; set; } public DateTime RaisedAtUtc { get; set; } public DateTime? AcknowledgedAtUtc { get; set; } public DateTime? ResolvedAtUtc { get; set; } }
/// <summary>Represents an immutable audit log entry.</summary>
public class AuditLogEntry : BaseEntity { public AuditActorType ActorType { get; set; } public string? ActorId { get; set; } public Guid? ActorPlayerProfileId { get; set; } public string Action { get; set; } = string.Empty; public string? EntityName { get; set; } public Guid? EntityId { get; set; } public string? DetailsJson { get; set; } public DateTime OccurredAtUtc { get; set; } }
/// <summary>Represents a stored idempotency key for replayable HTTP commands.</summary>
public class IdempotencyKey : BaseEntity { public Guid? IdentityUserId { get; set; } public Guid? PlayerProfileId { get; set; } public string OperationName { get; set; } = string.Empty; public string Key { get; set; } = string.Empty; public string RequestHash { get; set; } = string.Empty; public int? ResponseStatusCode { get; set; } public string? ResponseBodyHash { get; set; } public DateTime ExpiresAtUtc { get; set; } }
/// <summary>Represents a WhatsApp one-time sign-in challenge.</summary>
public class WhatsAppSignInChallenge : BaseEntity { public Guid? PlayerProfileId { get; set; } public string ChallengeId { get; set; } = string.Empty; public string ChallengeTokenHash { get; set; } = string.Empty; public string? PhoneNumberHash { get; set; } public string? MaskedPhoneNumber { get; set; } public string CallbackUriHash { get; set; } = string.Empty; public DateTime ExpiresAtUtc { get; set; } public DateTime? ConsumedAtUtc { get; set; } }
/// <summary>Represents a hashed refresh token and its rotation family.</summary>
public class RefreshToken : BaseEntity
{
    /// <summary>Gets or sets the ASP.NET Identity user id that owns this token.</summary>
    public Guid IdentityUserId { get; set; }
    /// <summary>Gets or sets the optional linked player profile id.</summary>
    public Guid? PlayerProfileId { get; set; }
    /// <summary>Gets or sets the hash of the refresh token secret. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>Gets or sets the token family id used for rotation and family revocation.</summary>
    public Guid FamilyId { get; set; }
    /// <summary>Gets or sets the optional device/session identifier presented by the client.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Gets or sets a hash of the user agent observed when the token was issued.</summary>
    public string? UserAgentHash { get; set; }
    /// <summary>Gets or sets a hash of the remote IP observed when the token was issued.</summary>
    public string? IpAddressHash { get; set; }
    /// <summary>Gets or sets when this token expires.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>Gets or sets when this token was consumed during successful rotation.</summary>
    public DateTime? ConsumedAtUtc { get; set; }
    /// <summary>Gets or sets when this token was revoked.</summary>
    public DateTime? RevokedAtUtc { get; set; }
    /// <summary>Gets or sets the reason this token was revoked.</summary>
    public string? RevocationReason { get; set; }
    /// <summary>Gets or sets when reuse of this already-consumed token was detected.</summary>
    public DateTime? ReuseDetectedAtUtc { get; set; }
    /// <summary>Gets or sets the refresh token that replaced this token during rotation.</summary>
    public Guid? ReplacedByRefreshTokenId { get; set; }
    /// <summary>Gets or sets the token whose reuse caused this token to be revoked as part of the family.</summary>
    public Guid? RevokedByRefreshTokenId { get; set; }
}
