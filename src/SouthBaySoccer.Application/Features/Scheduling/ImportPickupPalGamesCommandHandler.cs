using System.Text.Json;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
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
/// Imports Pickup Pal active games as sessions. Pickup Pal is the source of truth: a game that
/// matches a session it previously created (by snapshot or occurrence key) adopts and overwrites
/// that session; otherwise a new session is created. A manually-created session is never adopted
/// just because it shares a start time. Each game's sanitized payload and participant roster are
/// persisted alongside the session, and only future active games with capacity are published.
/// </summary>
public sealed class ImportPickupPalGamesCommandHandler(
    IPickupPalGamesClient gamesClient,
    IPickupPalGameRepository gameRepository,
    ISessionRepository sessionRepository,
    ISeasonRepository seasonRepository,
    IVenueRepository venueRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string ImportedVenueLocality = "Imported from Pickup Pal";
    private const string ActiveGameStatus = "active";

    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PickupPalImportResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        var games = await gamesClient.GetActiveGamesAsync(cancellationToken);
        if (games.Count == 0)
        {
            return new PickupPalImportResult(0, 0, []);
        }

        var seasons = await seasonRepository.ListActiveAsync(cancellationToken);
        var imported = 0;
        var warnings = new List<string>();

        foreach (var game in games)
        {
            var season = seasons.FirstOrDefault(x =>
                x.StartsAtUtc <= game.StartsAtUtc && x.EndsAtUtc >= game.StartsAtUtc);
            if (season is null)
            {
                warnings.Add($"Skipped Pickup Pal game {game.Id}: no season covers its start date.");
                continue;
            }

            var publish = ShouldPublish(game, out var publishWarning);
            if (publishWarning is not null)
            {
                warnings.Add(publishWarning);
            }

            await ImportGameAsync(game, season.Id, publish, cancellationToken);
            imported++;
        }

        if (imported > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new PickupPalImportResult(imported, games.Count - imported, warnings);
    }

    private async Task ImportGameAsync(PickupPalGame game, Guid seasonId, bool publish, CancellationToken cancellationToken)
    {
        var occurrenceKey = BuildOccurrenceKey(game.Id);
        var snapshot = await gameRepository.FindSnapshotByGameIdAsync(game.Id, cancellationToken);

        // Only adopt a session this import previously created — matched by its snapshot or its
        // Pickup Pal occurrence key. A manual session that merely coincides in start time is left
        // untouched; a new imported session is created for the game instead.
        var session = snapshot is not null
            ? await sessionRepository.GetByIdAsync(snapshot.SessionId, cancellationToken)
            : null;
        session ??= await sessionRepository.FindByOccurrenceKeyAsync(occurrenceKey, cancellationToken);

        var venue = await ResolveOrCreateVenueAsync(game.Location, cancellationToken);

        if (session is null)
        {
            session = new Session { Id = Guid.NewGuid(), SeasonId = seasonId };
            ApplyGame(session, game, venue, occurrenceKey, publish);
            await sessionRepository.AddAsync(session, cancellationToken);
        }
        else
        {
            ApplyGame(session, game, venue, occurrenceKey, publish);
            sessionRepository.Update(session);
        }

        var sanitizedJson = JsonSerializer.Serialize(game, SnapshotJsonOptions);
        if (snapshot is null)
        {
            snapshot = new PickupPalGameSnapshot
            {
                Id = Guid.NewGuid(),
                PickupPalGameId = game.Id,
            };
            ApplySnapshot(snapshot, game, session.Id, sanitizedJson);
            await gameRepository.AddSnapshotAsync(snapshot, cancellationToken);
        }
        else
        {
            ApplySnapshot(snapshot, game, session.Id, sanitizedJson);
            gameRepository.UpdateSnapshot(snapshot);
        }

        var participants = game.Participants
            .Select((participant, index) => new PickupPalGameParticipant
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                PickupPalParticipantId = participant.Id,
                DisplayName = Truncate(participant.DisplayName, 160),
                IsGuest = participant.IsGuest,
                IsWaitlist = participant.IsWaitlist,
                DisplayOrder = index,
                JoinedAtUtc = participant.JoinedAtUtc,
            })
            .ToArray();
        await gameRepository.ReplaceParticipantsAsync(session.Id, participants, cancellationToken);
    }

    private void ApplySnapshot(
        PickupPalGameSnapshot snapshot,
        PickupPalGame game,
        Guid sessionId,
        string sanitizedJson)
    {
        snapshot.SessionId = sessionId;
        snapshot.CapturedAtUtc = clock.UtcNow;
        snapshot.StartsAtUtc = game.StartsAtUtc;
        snapshot.Location = Truncate(game.Location, 512);
        snapshot.MaxPlayers = game.MaxPlayers;
        snapshot.Status = Truncate(game.Status, 32);
        snapshot.GroupName = Truncate(game.GroupName, 160);
        snapshot.SanitizedGameJson = sanitizedJson;
    }

    private static void ApplyGame(Session session, PickupPalGame game, Venue venue, string occurrenceKey, bool publish)
    {
        session.VenueId = venue.Id;
        session.Title = BuildTitle(game);
        session.Format = GuessFormat(game.MaxPlayers);
        session.Capacity = Math.Max(game.MaxPlayers, 1);
        session.TeamCount = 2;
        session.StartsAtUtc = game.StartsAtUtc;
        session.CheckInOpensAtUtc = game.StartsAtUtc.AddMinutes(-30);
        session.CheckInClosesAtUtc = game.StartsAtUtc;
        session.RsvpDeadlineUtc = game.StartsAtUtc.AddHours(-1);
        session.OccurrenceKey = occurrenceKey;
        // Non-destructive: only promote to Published when the game validated as publishable. An
        // unpublishable game leaves a new session at its Draft default and never demotes an
        // already-published one.
        if (publish)
        {
            session.Status = SessionStatus.Published;
        }
    }

    // A game is published only when Pickup Pal reports it active, it still lies in the future, and
    // it carries a real capacity. Anything else is imported as a draft so an admin can review it,
    // and the reason is surfaced as an import warning.
    private bool ShouldPublish(PickupPalGame game, out string? warning)
    {
        warning = null;
        if (!string.Equals(game.Status, ActiveGameStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (game.MaxPlayers <= 0)
        {
            warning = $"Imported Pickup Pal game {game.Id} as draft: it reports no player capacity.";
            return false;
        }

        if (game.StartsAtUtc <= clock.UtcNow)
        {
            warning = $"Imported Pickup Pal game {game.Id} as draft: its start time is in the past.";
            return false;
        }

        return true;
    }

    private async Task<Venue> ResolveOrCreateVenueAsync(string location, CancellationToken cancellationToken)
    {
        var name = Truncate(string.IsNullOrWhiteSpace(location) ? "Pickup Pal venue" : location.Trim(), 160);
        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        var existing = venues.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = name,
            Locality = ImportedVenueLocality,
            Address = Truncate(location.Trim(), 512),
        };
        await venueRepository.AddAsync(venue, cancellationToken);
        return venue;
    }

    private static string BuildOccurrenceKey(string gameId) => $"pickuppal:{gameId}";

    private static string BuildTitle(PickupPalGame game)
    {
        var weekday = SessionAdminTimeZone.ToLocal(game.StartsAtUtc).DayOfWeek;
        var prefix = string.IsNullOrWhiteSpace(game.GroupName)
            ? Truncate(game.Location, 60)
            : game.GroupName;
        return Truncate($"{prefix} - {weekday} pickup", 160);
    }

    // Pickup Pal games do not carry a format; approximate one from the roster size so the session
    // card stays meaningful. Admins can edit the session afterwards, but the next import re-applies
    // the source-of-truth mapping.
    private static string GuessFormat(int maxPlayers) => maxPlayers switch
    {
        <= 10 => "5v5",
        <= 16 => "7v7",
        _ => "9v9",
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
