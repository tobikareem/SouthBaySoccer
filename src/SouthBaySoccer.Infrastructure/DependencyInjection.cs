using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Caching;
using SouthBaySoccer.Application.Abstractions.Maps;
using SouthBaySoccer.Application.Abstractions.Payments;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Application.Features.Idempotency;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Groups;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Idempotency;
using SouthBaySoccer.Infrastructure.Maps;
using SouthBaySoccer.Infrastructure.Caching;
using SouthBaySoccer.Infrastructure.Persistence;
using SouthBaySoccer.Infrastructure.Persistence.Interceptors;
using SouthBaySoccer.Infrastructure.Payments;
using SouthBaySoccer.Infrastructure.Repositories;
using SouthBaySoccer.Infrastructure.Scheduling;
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
        services.AddMemoryCache();
        services.AddScoped<CacheEvictionQueue>();
        services.AddScoped<IReadThroughCache, MemoryReadThroughCache>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPlayerProfileRepository, PlayerProfileRepository>();
        services.AddScoped<IWaiverRepository, WaiverRepository>();
        // Reference lists read on nearly every request path, decorated with a short cache that the
        // unit of work invalidates after a successful commit.
        services.AddScoped<SeasonRepository>();
        services.AddScoped<ISeasonRepository>(provider => new CachedSeasonRepository(
            provider.GetRequiredService<SeasonRepository>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<CacheEvictionQueue>()));
        services.AddScoped<VenueRepository>();
        services.AddScoped<IVenueRepository>(provider => new CachedVenueRepository(
            provider.GetRequiredService<VenueRepository>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<CacheEvictionQueue>()));
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IRsvpRepository, RsvpRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IStatsRepository, StatsRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.TryAddScoped<IPaymentGateway, UnavailablePaymentGateway>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.TryAddSingleton<IMapsService, UnavailableMapsService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IWhatsAppChallengeService, WhatsAppChallengeService>();
        services.AddSingleton<IWhatsAppChallengeTokenGenerator, WhatsAppChallengeTokenGenerator>();
        services.AddSingleton<IWhatsAppChallengeDeliverySender, UnavailableWhatsAppChallengeDeliverySender>();
        services.AddScoped<IWhatsAppIdentityResolver, WhatsAppIdentityResolver>();
        // Caps replace HttpClient's 100-second default so a slow Pickup Pal cannot hang requests.
        // The games client stays inside the import path's 5s budget; the user and group clients sit
        // on interactive sign-in/link flows, where 10s leaves headroom for the provider's own cold
        // starts while staying well under the mobile client's 30s timeout.
        services.AddHttpClient<IPickupPalUserClient, PickupPalUserClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient<IPickupPalGamesClient, PickupPalGamesClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(5));
        // Registered by concrete type so the caching decorator below owns the IPickupPalGroupClient
        // registration; the typed HttpClient plumbing is unchanged.
        services.AddHttpClient<PickupPalGroupClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IPickupPalGroupClient>(provider => new CachedPickupPalGroupClient(
            provider.GetRequiredService<PickupPalGroupClient>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<IClock>()));
        services.AddScoped<IPickupPalGameRepository, PickupPalGameRepository>();
        services.AddScoped<IGroupChatRepository, GroupChatRepository>();
        services.AddScoped<IPlayerGroupLinkRepository, PlayerGroupLinkRepository>();
        services.AddSingleton<IConfiguredAdminPhoneNumberService, ConfiguredAdminPhoneNumberService>();
        services.AddScoped<IPickupPalUserSyncService, PickupPalUserSyncService>();
        services.AddScoped<IAuthenticationTokenIssuer, AuthenticationTokenIssuer>();
        // Stateless after construction (immutable options + clock), so one instance serves all
        // requests instead of rebuilding the signing-key index per authenticated request.
        services.AddSingleton<ITokenService, JwtTokenService>();
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
                options.User.AllowedUserNameCharacters += ":";
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
