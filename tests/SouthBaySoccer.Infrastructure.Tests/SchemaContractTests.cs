using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Infrastructure.Persistence;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class SchemaContractTests
{
    private readonly InfrastructureDatabaseFixture database;

    public SchemaContractTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task RsvpResponses_WhenSchemaCreated_RejectsWaitlistedAndUsesRowVersion()
    {
        using var db = CreateDbContext();
        var checkDefinition = await GetCheckConstraintDefinitionAsync(db, "RsvpResponses", "CK_RsvpResponses_Status");
        var rowVersionColumns = await GetRowVersionColumnsAsync(db);

        checkDefinition.Should().Contain("Going").And.Contain("Maybe").And.Contain("NotGoing");
        checkDefinition.Should().NotContain("Waitlisted");
        rowVersionColumns.Should().Contain(("RsvpResponses", "RowVersion"));
    }

    [Fact]
    public async Task WaitlistEntries_WhenSchemaCreated_HasActiveFilteredUniqueness()
    {
        using var db = CreateDbContext();
        var indexes = await GetFilteredIndexesAsync(db, "WaitlistEntries");

        indexes.Should().Contain(i =>
            i.Name == "IX_WaitlistEntries_SessionId_PlayerProfileId" &&
            i.IsUnique &&
            i.Filter.Contains("[Status]") &&
            i.Filter.Contains("Active") &&
            i.Filter.Contains("[IsDeleted]"));
        indexes.Should().Contain(i =>
            i.Name == "IX_WaitlistEntries_SessionId_Position" &&
            i.IsUnique &&
            i.Filter.Contains("[Status]") &&
            i.Filter.Contains("Active") &&
            i.Filter.Contains("[IsDeleted]"));
    }

    [Fact]
    public async Task ProcessedWebhookEvents_WhenSchemaCreated_HasNonFilteredProviderEventUniqueness()
    {
        using var db = CreateDbContext();
        var indexes = await GetIndexesAsync(db, "ProcessedWebhookEvents");

        indexes.Should().Contain(i =>
            i.Name == "IX_ProcessedWebhookEvents_Provider_ProviderEventId" &&
            i.IsUnique &&
            !i.HasFilter);
    }

    [Fact]
    public async Task PlayerRatingVotes_WhenSchemaCreated_RejectsSelfVotesAndInvalidScores()
    {
        using var db = CreateDbContext();
        var selfVote = await GetCheckConstraintDefinitionAsync(db, "PlayerRatingVotes", "CK_PlayerRatingVotes_NoSelfVote");
        var score = await GetCheckConstraintDefinitionAsync(db, "PlayerRatingVotes", "CK_PlayerRatingVotes_Score");
        var indexes = await GetFilteredIndexesAsync(db, "PlayerRatingVotes");

        NormalizeSql(selfVote).Should().Contain("[VoterPlayerProfileId]<>[RatedPlayerProfileId]");
        NormalizeSql(score).Should().Contain("[Score]>=(0)").And.Contain("[Score]<=(10)");
        indexes.Should().Contain(i =>
            i.Name == "IX_PlayerRatingVotes_MatchId_VoterPlayerProfileId_RatedPlayerProfileId" &&
            i.IsUnique &&
            i.Filter.Contains("[IsDeleted]"));
    }

    [Fact]
    public async Task PlayerMatchStats_WhenSchemaCreated_StoresParticipationOnly()
    {
        using var db = CreateDbContext();
        var columns = await GetColumnsAsync(db, "PlayerMatchStats");

        columns.Should().Contain(new[] { "Played", "MinutesPlayed", "Started", "PlayedGoalkeeper", "Position" });
        columns.Should().NotContain(new[] { "Goals", "Assists", "Likes", "Rating", "MvpAwards" });
    }


    [Fact]
    public async Task RefreshTokens_WhenSchemaCreated_SupportsRotationReuseAndFamilyRevocation()
    {
        using var db = CreateDbContext();
        var columns = await GetColumnsAsync(db, "RefreshTokens");
        var indexes = await GetIndexesAsync(db, "RefreshTokens");
        var replacedConstraint = await GetCheckConstraintDefinitionAsync(db, "RefreshTokens", "CK_RefreshTokens_ReplacedOnlyAfterConsumption");
        var reuseConstraint = await GetCheckConstraintDefinitionAsync(db, "RefreshTokens", "CK_RefreshTokens_ReusedOnlyAfterConsumption");
        var reasonConstraint = await GetCheckConstraintDefinitionAsync(db, "RefreshTokens", "CK_RefreshTokens_RevocationReasonRequiresRevocation");

        columns.Should().Contain(new[]
        {
            "TokenHash",
            "FamilyId",
            "DeviceId",
            "UserAgentHash",
            "IpAddressHash",
            "ExpiresAtUtc",
            "ConsumedAtUtc",
            "RevokedAtUtc",
            "RevocationReason",
            "ReuseDetectedAtUtc",
            "ReplacedByRefreshTokenId",
            "RevokedByRefreshTokenId",
        });
        indexes.Should().Contain(i => i.Name == "IX_RefreshTokens_TokenHash" && i.IsUnique && !i.HasFilter);
        indexes.Should().Contain(i => i.Name == "IX_RefreshTokens_IdentityUserId_FamilyId_ReuseDetectedAtUtc");
        indexes.Should().Contain(i => i.Name == "IX_RefreshTokens_ReplacedByRefreshTokenId" && i.IsUnique && i.HasFilter);
        NormalizeSql(replacedConstraint).Should().Contain("[ReplacedByRefreshTokenId]ISNULLOR[ConsumedAtUtc]ISNOTNULL");
        NormalizeSql(reuseConstraint).Should().Contain("[ReuseDetectedAtUtc]ISNULLOR[ConsumedAtUtc]ISNOTNULL");
        NormalizeSql(reasonConstraint).Should().Contain("[RevocationReason]ISNULLOR[RevokedAtUtc]ISNOTNULL");
    }
    [Fact]
    public async Task NotificationRecipients_WhenSchemaCreated_PreventsDuplicateDestinationsPerMessageAndChannel()
    {
        using var db = CreateDbContext();
        var indexes = await GetIndexesAsync(db, "NotificationRecipients");

        indexes.Should().Contain(i =>
            i.Name == "IX_NotificationRecipients_NotificationMessageId_Channel_DestinationHash" &&
            i.IsUnique &&
            !i.HasFilter);
    }

    [Fact]
    public void SoftDeleteFilters_WhenModelBuilt_AreAppliedOnlyToMutableTables()
    {
        using var db = CreateDbContext();

        db.Model.FindEntityType(typeof(PlayerProfile))!.GetDeclaredQueryFilters().Should().NotBeEmpty();
        db.Model.FindEntityType(typeof(RsvpResponse))!.GetDeclaredQueryFilters().Should().NotBeEmpty();
        db.Model.FindEntityType(typeof(PlayerMatchStats))!.GetDeclaredQueryFilters().Should().NotBeEmpty();

        db.Model.FindEntityType(typeof(RefreshToken))!.GetDeclaredQueryFilters().Should().BeEmpty();
        db.Model.FindEntityType(typeof(ProcessedWebhookEvent))!.GetDeclaredQueryFilters().Should().BeEmpty();
        db.Model.FindEntityType(typeof(OutboxMessage))!.GetDeclaredQueryFilters().Should().BeEmpty();
        db.Model.FindEntityType(typeof(NotificationDelivery))!.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public void EnumProperties_WhenModelBuilt_AreStoredAsStrings()
    {
        using var db = CreateDbContext();

        db.Model.FindEntityType(typeof(RsvpResponse))!.FindProperty(nameof(RsvpResponse.Status))!.GetColumnType().Should().Be("nvarchar(32)");
        db.Model.FindEntityType(typeof(MatchEvent))!.FindProperty(nameof(MatchEvent.EventType))!.GetColumnType().Should().Be("nvarchar(32)");
        db.Model.FindEntityType(typeof(MatchAward))!.FindProperty(nameof(MatchAward.AwardType))!.GetColumnType().Should().Be("nvarchar(32)");
        db.Model.FindEntityType(typeof(NotificationRecipient))!.FindProperty(nameof(NotificationRecipient.Channel))!.GetColumnType().Should().Be("nvarchar(32)");
    }

    private SouthBaySoccerDbContext CreateDbContext() => database.CreateDbContext();

    private static async Task<string> GetCheckConstraintDefinitionAsync(DbContext db, string tableName, string constraintName)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT cc.definition
            FROM sys.check_constraints cc
            JOIN sys.tables t ON t.object_id = cc.parent_object_id
            WHERE t.name = @tableName AND cc.name = @constraintName;
            """;
        AddParameter(command, "@tableName", tableName);
        AddParameter(command, "@constraintName", constraintName);

        await db.Database.OpenConnectionAsync();
        var value = await command.ExecuteScalarAsync();
        return value?.ToString() ?? string.Empty;
    }

    private static async Task<IReadOnlyList<(string TableName, string ColumnName)>> GetRowVersionColumnsAsync(DbContext db)
    {
        var results = new List<(string TableName, string ColumnName)>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(object_id) AS TableName, name AS ColumnName
            FROM sys.columns
            WHERE system_type_id = 189;
            """;

        await db.Database.OpenConnectionAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(DbContext db, string tableName)
    {
        var results = new List<IndexInfo>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT i.name, i.is_unique, i.has_filter, COALESCE(i.filter_definition, '')
            FROM sys.indexes i
            WHERE OBJECT_NAME(i.object_id) = @tableName AND i.name IS NOT NULL;
            """;
        AddParameter(command, "@tableName", tableName);

        await db.Database.OpenConnectionAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new IndexInfo(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<IndexInfo>> GetFilteredIndexesAsync(DbContext db, string tableName)
    {
        var indexes = await GetIndexesAsync(db, tableName);
        return indexes.Where(i => i.HasFilter).ToList();
    }

    private static async Task<IReadOnlyList<string>> GetColumnsAsync(DbContext db, string tableName)
    {
        var results = new List<string>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT c.name
            FROM sys.columns c
            WHERE OBJECT_NAME(c.object_id) = @tableName;
            """;
        AddParameter(command, "@tableName", tableName);

        await db.Database.OpenConnectionAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static string NormalizeSql(string sql) => sql.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static void AddParameter(System.Data.Common.DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record IndexInfo(string Name, bool IsUnique, bool HasFilter, string Filter);
}



