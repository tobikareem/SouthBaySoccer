using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Entities.Compliance;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Identity;

namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>Configures the SouthBaySoccer EF Core model.</summary>
internal static class SouthBaySoccerModelConfiguration
{
    public static void ConfigureSouthBaySoccerModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationIdentityUser>(b =>
        {
            b.ToTable("AspNetUsers");
            b.HasIndex(x => x.PlayerProfileId).IsUnique().HasFilter("[PlayerProfileId] IS NOT NULL");
        });

        ConfigureIdentity(modelBuilder);
        ConfigureCompliance(modelBuilder);
        ConfigurePayments(modelBuilder);
        ConfigureScheduling(modelBuilder);
        ConfigureStats(modelBuilder);
        ConfigureOperations(modelBuilder);
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerProfile>(b =>
        {
            ConfigureBase(b, "PlayerProfiles", softDelete: true);
            b.Property(x => x.PickupPalUserId).HasMaxLength(128);
            b.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            b.Property(x => x.NormalizedDisplayName).HasMaxLength(160).IsRequired();
            b.Property(x => x.PreferredPosition).HasMaxLength(64).IsRequired();
            b.Property(x => x.PhoneNumberHash).HasMaxLength(128);
            b.Property(x => x.MaskedPhoneNumber).HasMaxLength(32);
            b.Property(x => x.WhatsAppJidHash).HasMaxLength(128);
            b.Property(x => x.PhotoUri).HasMaxLength(1024);
            b.Property(x => x.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            b.HasIndex(x => x.IdentityUserId).IsUnique().HasFilter("[IdentityUserId] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(x => x.PickupPalUserId).IsUnique().HasFilter("[PickupPalUserId] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(x => x.PhoneNumberHash).HasFilter("[PhoneNumberHash] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(x => x.WhatsAppJidHash).HasFilter("[WhatsAppJidHash] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(x => new { x.IsGuest, x.IsDeleted });
        });
        modelBuilder.Entity<EmergencyContact>(b => { ConfigureBase(b, "EmergencyContacts", true); b.Property(x => x.Name).HasMaxLength(160).IsRequired(); b.Property(x => x.PhoneNumberHash).HasMaxLength(128).IsRequired(); b.Property(x => x.MaskedPhoneNumber).HasMaxLength(32).IsRequired(); b.Property(x => x.Relationship).HasMaxLength(80); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.PlayerProfileId).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<ProfileMerge>(b => { ConfigureBase(b, "ProfileMerges", false); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.MergedByActorType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.MergedByActorId).HasMaxLength(128); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.SourcePlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.TargetPlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.SourcePlayerProfileId).IsUnique().HasFilter("[Status] = 'Completed'"); b.ToTable(t => t.HasCheckConstraint("CK_ProfileMerges_SourceTarget", "[SourcePlayerProfileId] <> [TargetPlayerProfileId]")); });
    }

    private static void ConfigureCompliance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WaiverDocument>(b => { ConfigureBase(b, "WaiverDocuments", true); b.Property(x => x.Version).HasMaxLength(32).IsRequired(); b.Property(x => x.Title).HasMaxLength(200).IsRequired(); b.Property(x => x.ContentHash).HasMaxLength(128).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => x.Version).IsUnique(); b.HasIndex(x => x.Status).IsUnique().HasFilter("[Status] = 'Published' AND [IsDeleted] = 0"); });
        modelBuilder.Entity<WaiverAcceptance>(b => { ConfigureBase(b, "WaiverAcceptances", false); b.Property(x => x.ContentHash).HasMaxLength(128).IsRequired(); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<WaiverDocument>().WithMany().HasForeignKey(x => x.WaiverDocumentId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.PlayerProfileId, x.WaiverDocumentId }).IsUnique(); b.HasIndex(x => new { x.PlayerProfileId, x.AcceptedAtUtc }); });
    }

    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StripeCustomerReference>(b => { ConfigureBase(b, "StripeCustomerReferences", true); b.Property(x => x.StripeCustomerId).HasMaxLength(128).IsRequired(); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.StripeCustomerId).IsUnique(); b.HasIndex(x => x.PlayerProfileId).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<Membership>(b => { ConfigureBase(b, "Memberships", true); b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.StripeSubscriptionId).HasMaxLength(128); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.PlayerProfileId).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.PaymentStatus, x.EligibleUntilUtc }); });
        modelBuilder.Entity<PaymentLedgerEntry>(b => { ConfigureBase(b, "PaymentLedgerEntries", false); b.Property(x => x.EntryType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.Provider).HasMaxLength(32).IsRequired(); b.Property(x => x.ProviderReferenceId).HasMaxLength(160).IsRequired(); b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasOne<ProcessedWebhookEvent>().WithMany().HasForeignKey(x => x.ProcessedWebhookEventId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.PlayerProfileId, x.OccurredAtUtc }); b.HasIndex(x => new { x.SessionId, x.PlayerProfileId }); b.HasIndex(x => new { x.Provider, x.ProviderReferenceId }).IsUnique(); });
        modelBuilder.Entity<ProcessedWebhookEvent>(b => { ConfigureBase(b, "ProcessedWebhookEvents", false); b.Property(x => x.Provider).HasMaxLength(32).IsRequired(); b.Property(x => x.ProviderEventId).HasMaxLength(160).IsRequired(); b.Property(x => x.EventType).HasMaxLength(128).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique(); b.HasIndex(x => x.ProcessedAtUtc); });
    }

    private static void ConfigureScheduling(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Season>(b => { ConfigureBase(b, "Seasons", true); b.Property(x => x.Name).HasMaxLength(80).IsRequired(); b.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<Venue>(b => { ConfigureBase(b, "Venues", true); b.Property(x => x.Name).HasMaxLength(160).IsRequired(); b.Property(x => x.Locality).HasMaxLength(160).IsRequired(); b.Property(x => x.Address).HasMaxLength(512); b.Property(x => x.MapsProviderReference).HasMaxLength(256); });
        modelBuilder.Entity<RecurrenceRule>(b => { ConfigureBase(b, "RecurrenceRules", true); b.Property(x => x.Name).HasMaxLength(160).IsRequired(); b.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired(); b.Property(x => x.Rule).HasMaxLength(2048).IsRequired(); });
        modelBuilder.Entity<Session>(b => { ConfigureBase(b, "Sessions", true); b.Property(x => x.Title).HasMaxLength(160).IsRequired(); b.Property(x => x.Format).HasMaxLength(64).IsRequired(); b.Property(x => x.OccurrenceKey).HasMaxLength(256); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion(); b.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Venue>().WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Restrict); b.HasOne<RecurrenceRule>().WithMany().HasForeignKey(x => x.RecurrenceRuleId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.OccurrenceKey).IsUnique().HasFilter("[OccurrenceKey] IS NOT NULL AND [IsDeleted] = 0"); b.HasIndex(x => x.StartsAtUtc); b.HasIndex(x => new { x.SeasonId, x.StartsAtUtc }); b.HasIndex(x => new { x.Status, x.StartsAtUtc }); b.ToTable(t => { t.HasCheckConstraint("CK_Sessions_Capacity", "[Capacity] > 0"); t.HasCheckConstraint("CK_Sessions_Deadline", "[RsvpDeadlineUtc] < [StartsAtUtc]"); }); });
        modelBuilder.Entity<RsvpResponse>(b => { ConfigureBase(b, "RsvpResponses", true); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.SessionId, x.PlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.SessionId, x.Status }); b.ToTable(t => t.HasCheckConstraint("CK_RsvpResponses_Status", "[Status] IN ('Going','Maybe','NotGoing')")); });
        modelBuilder.Entity<WaitlistEntry>(b => { ConfigureBase(b, "WaitlistEntries", true); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.SessionId, x.PlayerProfileId }).IsUnique().HasFilter("[Status] = 'Active' AND [IsDeleted] = 0"); b.HasIndex(x => new { x.SessionId, x.Position }).IsUnique().HasFilter("[Status] = 'Active' AND [IsDeleted] = 0"); });
        modelBuilder.Entity<CheckIn>(b => { ConfigureBase(b, "CheckIns", true); b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.SessionId, x.PlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.SessionId, x.CheckedInAtUtc }); });
        modelBuilder.Entity<AdminOverride>(b => { ConfigureBase(b, "AdminOverrides", false); b.Property(x => x.OverrideType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1024).IsRequired(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<PickupPalGameSnapshot>(b => { ConfigureBase(b, "PickupPalGameSnapshots", true); b.Property(x => x.PickupPalGameId).HasMaxLength(160).IsRequired(); b.Property(x => x.Location).HasMaxLength(512).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired(); b.Property(x => x.GroupName).HasMaxLength(160); b.Property(x => x.SanitizedGameJson).IsRequired(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.PickupPalGameId).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => x.SessionId); });
        modelBuilder.Entity<PickupPalGameParticipant>(b => { ConfigureBase(b, "PickupPalGameParticipants", true); b.Property(x => x.PickupPalParticipantId).HasMaxLength(160).IsRequired(); b.Property(x => x.DisplayName).HasMaxLength(160).IsRequired(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.SessionId, x.PickupPalParticipantId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.SessionId, x.IsWaitlist, x.DisplayOrder }); b.HasIndex(x => x.PlayerProfileId); });
    }

    private static void ConfigureStats(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Match>(b => { ConfigureBase(b, "Matches", true); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.SessionId, x.MatchNumber }).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<MatchTeam>(b => { ConfigureBase(b, "MatchTeams", true); b.Property(x => x.Name).HasMaxLength(80).IsRequired(); b.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.MatchId, x.TeamNumber }).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<TeamAssignment>(b => { ConfigureBase(b, "TeamAssignments", true); b.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict); b.HasOne<MatchTeam>().WithMany().HasForeignKey(x => x.MatchTeamId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.MatchId, x.PlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.MatchTeamId, x.PlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<MatchResult>(b => { ConfigureBase(b, "MatchResults", true); b.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict); b.HasOne<MatchTeam>().WithMany().HasForeignKey(x => x.MatchTeamId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.MatchId, x.MatchTeamId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.ToTable(t => t.HasCheckConstraint("CK_MatchResults_NonNegative", "[Wins] >= 0 AND [Draws] >= 0 AND [Losses] >= 0 AND [GoalsFor] >= 0 AND [GoalsAgainst] >= 0")); });
        modelBuilder.Entity<PlayerMatchStats>(b => { ConfigureBase(b, "PlayerMatchStats", true); b.Property(x => x.Position).HasMaxLength(64); b.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.MatchId, x.PlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); });
        modelBuilder.Entity<MatchEvent>(b => { ConfigureBase(b, "MatchEvents", true); b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.ReviewNote).HasMaxLength(1024); b.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.PlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.AssistPlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.SubmittedByPlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.ReviewedByPlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.MatchId, x.EventType }); b.HasIndex(x => new { x.MatchId, x.ReviewStatus }); b.HasIndex(x => new { x.PlayerProfileId, x.EventType }); b.ToTable(t => { t.HasCheckConstraint("CK_MatchEvents_Minute", "[Minute] >= 0"); t.HasCheckConstraint("CK_MatchEvents_AssistOnlyOnGoal", "[AssistPlayerProfileId] IS NULL OR [EventType] = 'Goal'"); }); });
        modelBuilder.Entity<PlayerRatingVote>(b => { ConfigureBase(b, "PlayerRatingVotes", true); b.HasIndex(x => new { x.MatchId, x.VoterPlayerProfileId, x.RatedPlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.ToTable(t => { t.HasCheckConstraint("CK_PlayerRatingVotes_Score", "[Score] >= 0 AND [Score] <= 10"); t.HasCheckConstraint("CK_PlayerRatingVotes_NoSelfVote", "[VoterPlayerProfileId] <> [RatedPlayerProfileId]"); }); });
        modelBuilder.Entity<PlayerLike>(b => { ConfigureBase(b, "PlayerLikes", true); b.HasIndex(x => new { x.MatchId, x.GiverPlayerProfileId, x.ReceiverPlayerProfileId }).IsUnique().HasFilter("[IsDeleted] = 0"); b.ToTable(t => t.HasCheckConstraint("CK_PlayerLikes_NoSelfLike", "[GiverPlayerProfileId] <> [ReceiverPlayerProfileId]")); });
        modelBuilder.Entity<MatchAward>(b => { ConfigureBase(b, "MatchAwards", true); b.Property(x => x.AwardType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.HasIndex(x => new { x.MatchId, x.AwardType }).IsUnique().HasFilter("[AwardType] = 'Mvp' AND [IsDeleted] = 0"); });
        modelBuilder.Entity<StatCorrection>(b => { ConfigureBase(b, "StatCorrections", false); b.Property(x => x.Reason).HasMaxLength(1024).IsRequired(); b.Property(x => x.BeforeJson).HasMaxLength(4000).IsRequired(); b.Property(x => x.AfterJson).HasMaxLength(4000).IsRequired(); });
        modelBuilder.Entity<ProfileStatReassignmentAudit>(b => { ConfigureBase(b, "ProfileStatReassignmentAudits", false); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.SourceGuestPlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasOne<PlayerProfile>().WithMany().HasForeignKey(x => x.TargetPlayerProfileId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.SourceGuestPlayerProfileId, x.TargetPlayerProfileId, x.ReassignedAtUtc }); b.ToTable(t => t.HasCheckConstraint("CK_ProfileStatReassignmentAudits_AffectedCount", "[AffectedCount] >= 0")); });
    }

    private static void ConfigureOperations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>(b =>
        {
            ConfigureBase(b, "RefreshTokens", false);
            b.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
            b.Property(x => x.DeviceId).HasMaxLength(128);
            b.Property(x => x.UserAgentHash).HasMaxLength(256);
            b.Property(x => x.IpAddressHash).HasMaxLength(128);
            b.Property(x => x.RevocationReason).HasMaxLength(256);
            b.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedByRefreshTokenId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.RevokedByRefreshTokenId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => new { x.IdentityUserId, x.FamilyId, x.RevokedAtUtc, x.ExpiresAtUtc });
            b.HasIndex(x => new { x.IdentityUserId, x.FamilyId, x.ReuseDetectedAtUtc });
            b.HasIndex(x => x.ReplacedByRefreshTokenId).IsUnique().HasFilter("[ReplacedByRefreshTokenId] IS NOT NULL");
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_RefreshTokens_ReplacedByNotSelf", "[ReplacedByRefreshTokenId] IS NULL OR [ReplacedByRefreshTokenId] <> [Id]");
                t.HasCheckConstraint("CK_RefreshTokens_RevokedByNotSelf", "[RevokedByRefreshTokenId] IS NULL OR [RevokedByRefreshTokenId] <> [Id]");
                t.HasCheckConstraint("CK_RefreshTokens_ReplacedOnlyAfterConsumption", "[ReplacedByRefreshTokenId] IS NULL OR [ConsumedAtUtc] IS NOT NULL");
                t.HasCheckConstraint("CK_RefreshTokens_ReusedOnlyAfterConsumption", "[ReuseDetectedAtUtc] IS NULL OR [ConsumedAtUtc] IS NOT NULL");
                t.HasCheckConstraint("CK_RefreshTokens_RevocationReasonRequiresRevocation", "[RevocationReason] IS NULL OR [RevokedAtUtc] IS NOT NULL");
            });
        });
        modelBuilder.Entity<WhatsAppSignInChallenge>(b => { ConfigureBase(b, "WhatsAppSignInChallenges", false); b.Property(x => x.ChallengeId).HasMaxLength(128).IsRequired(); b.Property(x => x.ChallengeTokenHash).HasMaxLength(256).IsRequired(); b.Property(x => x.PhoneNumberHash).HasMaxLength(128); b.Property(x => x.MaskedPhoneNumber).HasMaxLength(32); b.Property(x => x.CallbackUriHash).HasMaxLength(256).IsRequired(); b.HasIndex(x => x.ChallengeTokenHash).IsUnique(); b.HasIndex(x => new { x.PhoneNumberHash, x.ExpiresAtUtc }); });
        modelBuilder.Entity<IdempotencyKey>(b => { ConfigureBase(b, "IdempotencyKeys", false); b.Property(x => x.OperationName).HasMaxLength(128).IsRequired(); b.Property(x => x.Key).HasMaxLength(160).IsRequired(); b.Property(x => x.RequestHash).HasMaxLength(256).IsRequired(); b.Property(x => x.ResponseBodyHash).HasMaxLength(256); b.Property(x => x.ResponseBodyJson).HasMaxLength(4000); b.HasIndex(x => new { x.IdentityUserId, x.OperationName, x.Key }).IsUnique(); });
        modelBuilder.Entity<OutboxMessage>(b => { ConfigureBase(b, "OutboxMessages", false); b.Property(x => x.MessageType).HasMaxLength(160).IsRequired(); b.Property(x => x.PayloadJson).HasMaxLength(4000).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.LockToken).HasMaxLength(128); b.Property(x => x.DeadLetterReason).HasMaxLength(1024); b.Property(x => x.CorrelationId).HasMaxLength(128); b.Property(x => x.IdempotencyKey).HasMaxLength(160); b.HasIndex(x => new { x.Status, x.AvailableAtUtc, x.CreatedAt }); b.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL"); });
        modelBuilder.Entity<NotificationMessage>(b => { ConfigureBase(b, "NotificationMessages", false); b.Property(x => x.TemplateKey).HasMaxLength(160).IsRequired(); b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.Subject).HasMaxLength(256); b.Property(x => x.PayloadJson).HasMaxLength(4000).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.IdempotencyKey).HasMaxLength(160); b.HasIndex(x => new { x.Status, x.ScheduledForUtc, x.Priority }); });
        modelBuilder.Entity<NotificationRecipient>(b => { ConfigureBase(b, "NotificationRecipients", false); b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.DestinationHash).HasMaxLength(256).IsRequired(); b.Property(x => x.MaskedDestination).HasMaxLength(160).IsRequired(); b.HasIndex(x => new { x.NotificationMessageId, x.Channel, x.DestinationHash }).IsUnique(); });
        modelBuilder.Entity<NotificationDelivery>(b => { ConfigureBase(b, "NotificationDeliveries", false); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.ProviderMessageId).HasMaxLength(256); b.Property(x => x.FailureCategory).HasMaxLength(160); b.HasIndex(x => new { x.NotificationRecipientId, x.AttemptNumber }).IsUnique(); b.HasIndex(x => new { x.Status, x.NextRetryAtUtc }); b.HasIndex(x => x.ProviderMessageId).IsUnique().HasFilter("[ProviderMessageId] IS NOT NULL"); });
        modelBuilder.Entity<AlertRule>(b => { ConfigureBase(b, "AlertRules", false); b.Property(x => x.Name).HasMaxLength(160).IsRequired(); b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32).IsRequired(); });
        modelBuilder.Entity<AlertInstance>(b => { ConfigureBase(b, "AlertInstances", false); b.Property(x => x.DeduplicationKey).HasMaxLength(256).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.ContextJson).HasMaxLength(4000); b.HasIndex(x => new { x.AlertRuleId, x.DeduplicationKey }).IsUnique().HasFilter("[Status] IN ('Open','Acknowledged')"); });
        modelBuilder.Entity<AuditLogEntry>(b => { ConfigureBase(b, "AuditLogEntries", false); b.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(32).IsRequired(); b.Property(x => x.ActorId).HasMaxLength(128); b.Property(x => x.Action).HasMaxLength(160).IsRequired(); b.Property(x => x.EntityName).HasMaxLength(160); b.Property(x => x.DetailsJson).HasMaxLength(4000); b.HasIndex(x => new { x.EntityName, x.EntityId, x.OccurredAtUtc }); b.HasIndex(x => new { x.ActorPlayerProfileId, x.OccurredAtUtc }); });
    }

    private static void ConfigureBase<T>(EntityTypeBuilder<T> b, string tableName, bool softDelete) where T : BaseEntity
    {
        b.ToTable(tableName);
        b.HasKey(x => x.Id);
        b.HasAnnotation(SouthBaySoccerEfAnnotations.UsesSoftDelete, softDelete);
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(128);
        b.Property(x => x.UpdatedBy).HasMaxLength(128);
        if (softDelete)
        {
            b.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}

