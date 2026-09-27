using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>Outcome of one Pickup Pal active-games import pass.</summary>
public sealed record PickupPalImportResult(
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> Warnings)
{
    /// <summary>An import that did not run (for example when Pickup Pal was unreachable).</summary>
    public static PickupPalImportResult NotRun(string warning) => new(0, 0, [warning]);
}

/// <summary>
/// Imports Pickup Pal active games as sessions: fetches the active feed and upserts every game
/// through <see cref="IPickupPalGameImportService"/>, the same path the RSVP roster sync uses for a
/// single game, then commits.
/// </summary>
public sealed class ImportPickupPalGamesCommandHandler(
    IPickupPalGamesClient gamesClient,
    IPickupPalGameImportService importService,
    IUnitOfWork unitOfWork)
{
    public async Task<PickupPalImportResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        var games = await gamesClient.GetActiveGamesAsync(cancellationToken);
        var result = await importService.UpsertAsync(games, cancellationToken);
        if (result.ImportedCount > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
