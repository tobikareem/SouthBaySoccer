using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InfrastructureDatabaseCollection : ICollectionFixture<InfrastructureDatabaseFixture>
{
    public const string Name = "Infrastructure database";
}

public sealed class InfrastructureDatabaseFixture : IAsyncLifetime
{
    private const string TestDatabasePrefix = "SouthBaySoccer_Test_";

    public InfrastructureDatabaseFixture()
    {
        DatabaseName = $"{TestDatabasePrefix}{Guid.NewGuid():N}";
        ConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = DatabaseName,
            IntegratedSecurity = true,
            MultipleActiveResultSets = true,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public string ConnectionString { get; }

    private string DatabaseName { get; }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!DatabaseName.StartsWith(TestDatabasePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to drop non-test database '{DatabaseName}'.");
        }

        await using var connection = new SqlConnection(GetMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    // Retry-on-failure matches production (AddInfrastructure). Without it the suite cannot see
    // execution-strategy bugs — e.g. user-initiated transactions outside
    // CreateExecutionStrategy().ExecuteAsync pass here but throw in production
    // (.ai/lessons/2026-07-21-ef-retry-strategy-manual-transactions.md).
    public SouthBaySoccerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SouthBaySoccerDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new SouthBaySoccerDbContext(options);
    }

    public SouthBaySoccerDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SouthBaySoccerDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure());

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new SouthBaySoccerDbContext(optionsBuilder.Options);
    }

    private string GetMasterConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "master",
        };

        return builder.ConnectionString;
    }
}
