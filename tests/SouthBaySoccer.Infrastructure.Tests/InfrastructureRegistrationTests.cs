using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Maps;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class InfrastructureRegistrationTests
{
    private readonly InfrastructureDatabaseFixture database;

    public InfrastructureRegistrationTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public void AddInfrastructure_WhenRequestServicesAreNotRegistered_ProvidesFallbackAuditServices()
    {
        using var provider = CreateServiceProvider();

        provider.GetRequiredService<IClock>().UtcNow.Kind.Should().Be(DateTimeKind.Utc);
        provider.GetRequiredService<ICurrentUser>().UserId.Should().BeNull();
        provider.GetRequiredService<SouthBaySoccerDbContext>().Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_WhenRegistered_ProvidesIdentityCoreStoresRolesAndTokenProviders()
    {
        using var provider = CreateServiceProvider();

        provider.GetRequiredService<UserManager<ApplicationIdentityUser>>().Should().NotBeNull();
        provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>().Should().NotBeNull();
        provider.GetRequiredService<IUserStore<ApplicationIdentityUser>>().Should().NotBeNull();
        provider.GetRequiredService<IRoleStore<IdentityRole<Guid>>>().Should().NotBeNull();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        options.Tokens.ProviderMap.Should().ContainKey(TokenOptions.DefaultProvider);
        options.SignIn.RequireConfirmedAccount.Should().BeTrue();
        options.User.RequireUniqueEmail.Should().BeTrue();
        options.Password.RequiredLength.Should().Be(10);
    }

    [Fact]
    public void AddInfrastructure_WhenRegistered_ProvidesEfBackedDataProtection()
    {
        using var provider = CreateServiceProvider();

        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("InfrastructureRegistrationTests");
        var protectedValue = protector.Protect("south-bay-soccer");

        protector.Unprotect(protectedValue).Should().Be("south-bay-soccer");
    }

    [Fact]
    public async Task IdentityService_CheckPasswordAsync_WhenPasswordMatches_ReturnsTrue()
    {
        using var provider = CreateServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<ApplicationIdentityUser>>();
        var identityService = provider.GetRequiredService<IIdentityService>();
        var user = new ApplicationIdentityUser
        {
            Id = Guid.NewGuid(),
            UserName = $"player-{Guid.NewGuid():N}@southbay.test",
            Email = $"player-{Guid.NewGuid():N}@southbay.test",
            EmailConfirmed = true,
        };
        var password = "SouthBay123";

        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));

        var matches = await identityService.CheckPasswordAsync(user.Id, password);

        matches.Should().BeTrue();
    }


    [Fact]
    public void AddInfrastructure_WhenRegistered_ProvidesSchedulingRepositoriesAndMapsFallback()
    {
        using var provider = CreateServiceProvider();

        provider.GetRequiredService<ISeasonRepository>().Should().NotBeNull();
        provider.GetRequiredService<IVenueRepository>().Should().NotBeNull();
        provider.GetRequiredService<ISessionRepository>().Should().NotBeNull();
        provider.GetRequiredService<IRsvpRepository>().Should().NotBeNull();
        provider.GetRequiredService<IMapsService>().Should().NotBeNull();
    }
    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }
}
