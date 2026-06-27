using SouthBaySoccer.Application.Abstractions.Authentication;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Fallback current-user context for non-human infrastructure work.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId => null;

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public bool IsInRole(string role) => false;

    /// <inheritdoc />
    public bool HasPolicy(string policy) => false;
}
