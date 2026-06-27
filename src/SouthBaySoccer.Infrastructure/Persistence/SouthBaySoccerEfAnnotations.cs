namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>
/// EF Core model annotation names used by SouthBaySoccer infrastructure services.
/// </summary>
internal static class SouthBaySoccerEfAnnotations
{
    /// <summary>
    /// Indicates whether deletes for the entity type should be converted to soft deletes.
    /// </summary>
    public const string UsesSoftDelete = "SouthBaySoccer:UsesSoftDelete";
}
