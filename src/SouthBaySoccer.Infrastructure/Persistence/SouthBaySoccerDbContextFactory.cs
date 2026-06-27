using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>Creates <see cref="SouthBaySoccerDbContext"/> instances for EF Core design-time commands.</summary>
public sealed class SouthBaySoccerDbContextFactory : IDesignTimeDbContextFactory<SouthBaySoccerDbContext>
{
    /// <inheritdoc />
    public SouthBaySoccerDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddUserSecrets<SouthBaySoccerDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SouthBaySoccerDb")
            ?? configuration["ConnectionStrings:SouthBaySoccerDb"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing ConnectionStrings:SouthBaySoccerDb. Set it with dotnet user-secrets for the Infrastructure project or the ConnectionStrings__SouthBaySoccerDb environment variable.");
        }

        var options = new DbContextOptionsBuilder<SouthBaySoccerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SouthBaySoccerDbContext(options);
    }
}
