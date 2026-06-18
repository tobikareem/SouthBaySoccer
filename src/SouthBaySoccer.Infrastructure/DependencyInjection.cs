using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure;

/// <summary>
/// Registers Infrastructure services (persistence and external providers) into the
/// dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="connectionString">The Azure SQL connection string.</param>
    /// <returns>The same service collection so that calls can be chained.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SouthBaySoccerDbContext>(o => o.UseAzureSql(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
