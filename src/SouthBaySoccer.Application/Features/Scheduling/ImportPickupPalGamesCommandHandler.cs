using System.Text.Json;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Identity;
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
/// persisted alongside the session, and active games with capacity are published.
/// </summary>
public sealed class ImportPickupPalGamesCommandHandler(
    IPickupPalGamesClient gamesClient,
    IPickupPalGameRepository gameRepository,
    ISessionRepository sessionRepository,
    ISeasonRepository seasonRepository,
    IVenueRepository venueRepository,
    IPlayerProfileRepository playerProfileRepository,
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

        // Profiles created during this pass are not visible to repository lookups until the final
        // SaveChanges, so the same person appearing on several games resolves through this cache.
        var profileCache = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);

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

            await ImportGameAsync(game, season.Id, publish, profileCache, cancellationToken);
            imported++;
        }

        if (imported > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new PickupPalImportResult(imported, games.Count - imported, warnings);
    }

    private async Task ImportGameAsync(
        PickupPalGame game,
        Guid seasonId,
        bool publish,
        Dictionary<string, PlayerProfile> profileCache,
        CancellationToken cancellationToken)
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

        var participants = new List<PickupPalGameParticipant>(game.Participants.Count);
        foreach (var (participant, index) in game.Participants.Select((p, i) => (p, i)))
        {
            var profile = await ResolveOrCreateProfileAsync(participant, profileCache, cancellationToken);
            participants.Add(new PickupPalGameParticipant
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                PickupPalParticipantId = participant.Id,
                PlayerProfileId = profile?.Id,
                DisplayName = Truncate(CleanDisplayName(participant.DisplayName), 160),
                IsGuest = participant.IsGuest,
                IsWaitlist = participant.IsWaitlist,
                DisplayOrder = index,
                JoinedAtUtc = participant.JoinedAtUtc,
            });
        }

        await gameRepository.ReplaceParticipantsAsync(session.Id, participants, cancellationToken);
    }

    /// <summary>
    /// Resolves the participant to a persistent <see cref="PlayerProfile"/> by Pickup Pal user id,
    /// then phone hash, then WhatsApp hash — creating one when no identity matches — so imported
    /// players appear in the player directory. Participants carrying no identity at all stay
    /// snapshot-only: without a stable key a profile could never be deduplicated across imports.
    /// </summary>
    private async Task<PlayerProfile?> ResolveOrCreateProfileAsync(
        PickupPalGameParticipantInfo participant,
        Dictionary<string, PlayerProfile> profileCache,
        CancellationToken cancellationToken)
    {
        var keys = BuildProfileCacheKeys(participant);
        if (keys.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (profileCache.TryGetValue(key, out var cached))
            {
                // A later game in the same pass may carry identity keys the first sighting lacked;
                // fold them in now so this import persists every key, not just the next one.
                BackfillIdentityKeys(cached, participant);
                CacheProfile(profileCache, keys, cached);
                return cached;
            }
        }

        PlayerProfile? profile = null;
        if (participant.UserId is { } userId)
        {
            profile = await playerProfileRepository.FindByPickupPalUserIdAsync(userId, cancellationToken);
        }

        if (profile is null && participant.PhoneNumberHash is { } phoneHash)
        {
            profile = await playerProfileRepository.FindByPhoneNumberHashAsync(phoneHash, cancellationToken);
            if (HasConflictingPickupPalUser(profile, participant.UserId))
            {
                profile = null;
            }
        }

        if (profile is null && participant.WhatsAppJidHash is { } jidHash)
        {
            profile = await playerProfileRepository.FindByWhatsAppJidHashAsync(jidHash, cancellationToken);
            if (HasConflictingPickupPalUser(profile, participant.UserId))
            {
                profile = null;
            }
        }

        // Last resort: some participants arrive with no user id, no phone, and only an opaque
        // WhatsApp id that a signed-in profile never carries, so no key can ever match and the same
        // person ends up with a second profile. Fall back to an unambiguous display-name match -
        // the repository returns null when the name is shared, so this never merges two people.
        if (profile is null && !string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            profile = await playerProfileRepository.FindSingleByNormalizedDisplayNameAsync(
                participant.DisplayName.Trim().ToUpperInvariant(),
                cancellationToken);
            if (HasConflictingPickupPalUser(profile, participant.UserId))
            {
                profile = null;
            }
        }

        var isNew = profile is null;
        profile ??= new PlayerProfile
        {
            Id = Guid.NewGuid(),
            PreferredPosition = string.Empty,
            IsGuest = participant.IsGuest,
            Role = participant.IsGuest ? PlayerRole.Guest : PlayerRole.Player,
        };

        if (isNew || profile.IdentityUserId is null)
        {
            // Import-owned profile: Pickup Pal is its source of truth, so refresh the name and
            // guest standing. Profiles claimed through sign-in keep the name and role that sign-in
            // sync maintains.
            profile.DisplayName = Truncate(CleanDisplayName(participant.DisplayName), 160);
            profile.NormalizedDisplayName = profile.DisplayName.ToUpperInvariant();
            profile.IsGuest = participant.IsGuest;
            profile.Role = participant.IsGuest ? PlayerRole.Guest : PlayerRole.Player;
        }

        // Backfill identity keys the profile is missing; never overwrite an existing link.
        BackfillIdentityKeys(profile, participant);

        if (isNew)
        {
            await playerProfileRepository.AddAsync(profile, cancellationToken);
        }
        else
        {
            playerProfileRepository.Update(profile);
        }

        CacheProfile(profileCache, keys, profile);
        return profile;
    }

    private static List<string> BuildProfileCacheKeys(PickupPalGameParticipantInfo participant)
    {
        var keys = new List<string>(3);
        if (participant.UserId is { } userId)
        {
            keys.Add($"user:{userId}");
            return keys;
        }

        if (participant.PhoneNumberHash is { } phoneHash)
        {
            keys.Add($"phone:{phoneHash}");
        }

        if (participant.WhatsAppJidHash is { } jidHash)
        {
            keys.Add($"jid:{jidHash}");
        }

        return keys;
    }

    // Never overwrites an existing link — only fills keys the profile is still missing — so it is
    // safe to call on both a freshly resolved profile and a cache hit from an earlier game.
    private static void BackfillIdentityKeys(PlayerProfile profile, PickupPalGameParticipantInfo participant)
    {
        profile.PickupPalUserId ??= participant.UserId;
        if (profile.PhoneNumberHash is null && participant.PhoneNumberHash is not null)
        {
            profile.PhoneNumberHash = participant.PhoneNumberHash;
            profile.MaskedPhoneNumber = participant.MaskedPhoneNumber;
        }

        profile.WhatsAppJidHash ??= participant.WhatsAppJidHash;
    }

    private static bool HasConflictingPickupPalUser(PlayerProfile? profile, string? participantUserId) =>
        profile?.PickupPalUserId is { } existingUserId
        && participantUserId is { } incomingUserId
        && !string.Equals(existingUserId, incomingUserId, StringComparison.Ordinal);

    private static void CacheProfile(
        Dictionary<string, PlayerProfile> profileCache,
        List<string> keys,
        PlayerProfile profile)
    {
        foreach (var key in keys)
        {
            profileCache[key] = profile;
        }
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
        session.CheckInOpensAtUtc = game.StartsAtUtc.AddMinutes(-10);
        session.CheckInClosesAtUtc = game.StartsAtUtc.AddMinutes(5);
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

    // Pickup Pal's active-games feed is authoritative for whether an imported game is active.
    // Games remain publishable through kickoff so opening Game Day at the scheduled start does not
    // turn a newly imported live game into a draft.
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

    /// <summary>
    /// Pickup Pal display names come straight from WhatsApp, so many are decorated with emoji or
    /// punctuation ("Jojo🦍", "‘M", "…"). Trim any non-letter/digit run from each end and, when
    /// nothing usable is left, fall back to "Guest" - internal characters and legitimate accented
    /// or non-Latin names (e.g. Yoruba) are preserved.
    /// </summary>
    private static string CleanDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Guest";
        }

        var trimmed = value.Trim();
        var start = 0;
        var end = trimmed.Length;
        while (start < end && !char.IsLetterOrDigit(trimmed[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(trimmed[end - 1]))
        {
            end--;
        }

        var result = trimmed[start..end];
        return result.Any(char.IsLetter) ? result : "Guest";
    }
}
