using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;
using SouthBaySoccer.Infrastructure.Persistence.Interceptors;
using SouthBaySoccer.Infrastructure.Time;

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
    /// <param name="connectionString">The SQL Server or Azure SQL connection string.</param>
    /// <returns>The same service collection so that calls can be chained.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.AddScoped<AuditSoftDeleteSaveChangesInterceptor>();
        services.AddDbContext<SouthBaySoccerDbContext>((serviceProvider, options) =>
        {
            options
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                .AddInterceptors(serviceProvider.GetRequiredService<AuditSoftDeleteSaveChangesInterceptor>());
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenExchangeService, RefreshTokenExchangeService>();
        services.AddSingleton<IRefreshTokenHasher, RefreshTokenHasher>();
        services.AddSingleton<IRefreshTokenSecretGenerator, RefreshTokenSecretGenerator>();

        services
            .AddDataProtection()
            .PersistKeysToDbContext<SouthBaySoccerDbContext>();

        services
            .AddIdentityCore<ApplicationIdentityUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SouthBaySoccerDbContext>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationIdentityUser>>(TokenOptions.DefaultProvider);

        return services;
    }
}
