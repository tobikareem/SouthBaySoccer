using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Functions.Dev;

public sealed class DevAuthFunctions(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    SouthBaySoccerDbContext dbContext,
    IClock clock,
    ITokenService tokenService)
{
    private const string LocalAdminEmail = "local-admin@southbaysoccer.test";
    private const string LocalAdminDisplayName = "Local Admin";

    [Function(nameof(CreateLocalAdminSession))]
    [AllowAnonymous]
    public async Task<HttpResponseData> CreateLocalAdminSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dev/local-admin-session")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        EnsureLocalDevAuthEnabled();

        var now = clock.UtcNow;
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == LocalAdminEmail.ToUpperInvariant(), cancellationToken);
        var profile = user?.PlayerProfileId is null
            ? null
            : await dbContext.PlayerProfiles.SingleOrDefaultAsync(x => x.Id == user.PlayerProfileId.Value, cancellationToken);

        if (user is null)
        {
            user = new ApplicationIdentityUser
            {
                Id = Guid.NewGuid(),
                UserName = LocalAdminEmail,
                NormalizedUserName = LocalAdminEmail.ToUpperInvariant(),
                Email = LocalAdminEmail,
                NormalizedEmail = LocalAdminEmail.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            };

            dbContext.Users.Add(user);
        }

        if (profile is null)
        {
            profile = new PlayerProfile
            {
                Id = Guid.NewGuid(),
                IdentityUserId = user.Id,
                DisplayName = LocalAdminDisplayName,
                NormalizedDisplayName = LocalAdminDisplayName.ToUpperInvariant(),
                PreferredPosition = "Midfielder",
                IsGuest = false,
                Role = PlayerRole.Owner,
                CreatedAt = now,
                CreatedBy = "dev-local-admin-session",
            };

            dbContext.PlayerProfiles.Add(profile);
            user.PlayerProfileId = profile.Id;
        }
        else
        {
            profile.IdentityUserId = user.Id;
            profile.DisplayName = LocalAdminDisplayName;
            profile.NormalizedDisplayName = LocalAdminDisplayName.ToUpperInvariant();
            profile.PreferredPosition = string.IsNullOrWhiteSpace(profile.PreferredPosition) ? "Midfielder" : profile.PreferredPosition;
            profile.IsGuest = false;
            profile.Role = PlayerRole.Owner;
            user.PlayerProfileId = profile.Id;
        }

        await EnsureRoleAsync(PlayerRole.Owner.ToString(), cancellationToken);
        await EnsureUserRoleAsync(user.Id, PlayerRole.Owner.ToString(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var policies = AuthenticationPolicyMapper.FromRoles([PlayerRole.Owner.ToString()]);
        var issued = tokenService.IssueAccessToken(new AccessTokenIssueRequest(user.Id, [PlayerRole.Owner.ToString()], policies));

        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new LocalAdminSessionResponse(
                issued.Token,
                issued.ExpiresAtUtc,
                user.Id,
                profile.Id,
                profile.DisplayName,
                PlayerRole.Owner.ToString(),
                policies),
            cancellationToken);
    }

    private void EnsureLocalDevAuthEnabled()
    {
        var enabled = configuration.GetValue<bool>("DevTools:EnableLocalAdminSession");
        var functionsEnvironment = configuration["AZURE_FUNCTIONS_ENVIRONMENT"];
        var isDevelopment = hostEnvironment.IsDevelopment()
            || string.Equals(functionsEnvironment, "Development", StringComparison.OrdinalIgnoreCase);
        if (!enabled || !isDevelopment)
        {
            throw new ForbiddenException();
        }
    }

    private async Task EnsureRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var normalizedRoleName = roleName.ToUpperInvariant();
        if (await dbContext.Roles.AnyAsync(x => x.NormalizedName == normalizedRoleName, cancellationToken))
        {
            return;
        }

        dbContext.Roles.Add(new IdentityRole<Guid>
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = normalizedRoleName,
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        });
    }

    private async Task EnsureUserRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken)
    {
        var normalizedRoleName = roleName.ToUpperInvariant();
        var roleId = await dbContext.Roles
            .Where(x => x.NormalizedName == normalizedRoleName)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (roleId == Guid.Empty)
        {
            var trackedRole = dbContext.Roles.Local.Single(x => x.NormalizedName == normalizedRoleName);
            roleId = trackedRole.Id;
        }

        if (await dbContext.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == roleId, cancellationToken))
        {
            return;
        }

        dbContext.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = userId,
            RoleId = roleId,
        });
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken: cancellationToken);
        return response;
    }

    private sealed record LocalAdminSessionResponse(
        string AccessToken,
        DateTime AccessTokenExpiresAtUtc,
        Guid IdentityUserId,
        Guid PlayerProfileId,
        string DisplayName,
        string Role,
        IReadOnlyList<string> Policies);
}