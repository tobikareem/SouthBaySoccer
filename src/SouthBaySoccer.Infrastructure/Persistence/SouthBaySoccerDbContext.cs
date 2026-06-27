using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Compliance;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Infrastructure.Identity;

namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>
/// EF Core unit-of-work context for SouthBaySoccer backend persistence.
/// </summary>
public class SouthBaySoccerDbContext : IdentityDbContext<ApplicationIdentityUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    /// <summary>Initializes a new instance of the <see cref="SouthBaySoccerDbContext"/> class.</summary>
    public SouthBaySoccerDbContext(DbContextOptions<SouthBaySoccerDbContext> options) : base(options) { }

    /// <summary>Gets or sets player profiles.</summary>
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<ProfileMerge> ProfileMerges => Set<ProfileMerge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<WhatsAppSignInChallenge> WhatsAppSignInChallenges => Set<WhatsAppSignInChallenge>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<WaiverDocument> WaiverDocuments => Set<WaiverDocument>();
    public DbSet<WaiverAcceptance> WaiverAcceptances => Set<WaiverAcceptance>();
    public DbSet<StripeCustomerReference> StripeCustomerReferences => Set<StripeCustomerReference>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<PaymentLedgerEntry> PaymentLedgerEntries => Set<PaymentLedgerEntry>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<RecurrenceRule> RecurrenceRules => Set<RecurrenceRule>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<RsvpResponse> RsvpResponses => Set<RsvpResponse>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<AdminOverride> AdminOverrides => Set<AdminOverride>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchTeam> MatchTeams => Set<MatchTeam>();
    public DbSet<TeamAssignment> TeamAssignments => Set<TeamAssignment>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<PlayerMatchStats> PlayerMatchStats => Set<PlayerMatchStats>();
    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();
    public DbSet<PlayerRatingVote> PlayerRatingVotes => Set<PlayerRatingVote>();
    public DbSet<PlayerLike> PlayerLikes => Set<PlayerLike>();
    public DbSet<MatchAward> MatchAwards => Set<MatchAward>();
    public DbSet<StatCorrection> StatCorrections => Set<StatCorrection>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<NotificationMessage> NotificationMessages => Set<NotificationMessage>();
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertInstance> AlertInstances => Set<AlertInstance>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SouthBaySoccerDbContext).Assembly);
        modelBuilder.ConfigureSouthBaySoccerModel();
    }
}
