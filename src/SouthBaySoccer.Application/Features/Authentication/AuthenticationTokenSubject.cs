namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Identifies the authenticated player for whom application tokens should be issued.
/// </summary>
/// <param name="IdentityUserId">The ASP.NET Identity user id.</param>
/// <param name="PlayerProfileId">The player profile id.</param>
/// <param name="Roles">The role names to place in authorization claims.</param>
public sealed record AuthenticationTokenSubject(
    Guid IdentityUserId,
    Guid PlayerProfileId,
    IReadOnlyList<string> Roles);

