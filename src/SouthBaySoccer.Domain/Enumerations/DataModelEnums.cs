namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>Identifies whether an audited write was made by a person or system component.</summary>
public enum AuditActorType { User, PlayerProfile, Webhook, Timer, Queue, Migration, System }
/// <summary>Represents a profile merge workflow state.</summary>
public enum ProfileMergeStatus { Pending, Completed, Rejected }
/// <summary>Represents whether a waiver document is draft or active.</summary>
public enum WaiverDocumentStatus { Draft, Published, Retired }
/// <summary>Identifies a payment ledger entry type.</summary>
public enum PaymentLedgerEntryType { Membership, DropIn, Refund, Adjustment }
/// <summary>Represents processed webhook handling status.</summary>
public enum WebhookProcessingStatus { Processed, Duplicate, Ignored, Failed }
/// <summary>Represents a session lifecycle state.</summary>
public enum SessionStatus { Draft, Published, Canceled, Completed }
/// <summary>Represents whether a waitlist row is active or consumed.</summary>
public enum WaitlistEntryStatus { Active, Promoted, Canceled, Expired }
/// <summary>Represents actual attendance outcome.</summary>
public enum AttendanceOutcome { CheckedIn, Late, NoShow, Excused }
/// <summary>Identifies an admin override kind.</summary>
public enum AdminOverrideType { Waiver, Payment, Deadline, CheckIn }
/// <summary>Represents a match lifecycle state.</summary>
public enum MatchStatus { Draft, InProgress, Completed, Published, Locked }
/// <summary>Identifies a match event kind.</summary>
public enum MatchEventType { Goal, OwnGoal, YellowCard, RedCard }
/// <summary>Identifies supported match award types.</summary>
public enum MatchAwardType { Mvp }
/// <summary>Identifies a notification channel.</summary>
public enum NotificationChannel { Email, Sms, WhatsApp, Push }
/// <summary>Represents notification message status.</summary>
public enum NotificationStatus { Pending, Scheduled, Sending, Sent, PartiallyFailed, Failed, Canceled }
/// <summary>Represents provider delivery status.</summary>
public enum NotificationDeliveryStatus { Pending, Sending, Delivered, RetryScheduled, Failed, DeadLettered }
/// <summary>Represents alert severity.</summary>
public enum AlertSeverity { Info, Warning, Error, Critical }
/// <summary>Represents alert lifecycle status.</summary>
public enum AlertStatus { Open, Acknowledged, Resolved, Suppressed }
/// <summary>Represents outbox dispatch status.</summary>
public enum OutboxMessageStatus { Pending, Processing, Processed, RetryScheduled, DeadLettered }
