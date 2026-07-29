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

        // Every lookup this pass needs is read up front in a fixed number of batched queries, so
        // the per-game and per-participant work below touches memory only. Without this the pass
        // issued four queries per participant plus a full venue read per game.
        var lookups = await PrefetchAsync(games, cancellationToken);

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

            await ImportGameAsync(game, season.Id, publish, lookups, profileCache, cancellationToken);
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
        ImportLookups lookups,
        Dictionary<string, PlayerProfile> profileCache,
        CancellationToken cancellationToken)
    {
        var occurrenceKey = BuildOccurrenceKey(game.Id);
        var snapshot = lookups.SnapshotsByGameId.GetValueOrDefault(game.Id);

        // Only adopt a session this import previously created — matched by its snapshot or its
        // Pickup Pal occurrence key. A manual session that merely coincides in start time is left
        // untouched; a new imported session is created for the game instead.
        var session = snapshot is not null
            ? lookups.SessionsById.GetValueOrDefault(snapshot.SessionId)
            : null;
        session ??= lookups.SessionsByOccurrenceKey.GetValueOrDefault(occurrenceKey);

        var venue = await ResolveOrCreateVenueAsync(game.Location, lookups, cancellationToken);

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
        else if (!SnapshotMatches(snapshot, game, session.Id, sanitizedJson))
        {
            ApplySnapshot(snapshot, game, session.Id, sanitizedJson);
            gameRepository.UpdateSnapshot(snapshot);
        }

        // An unchanged game writes no snapshot row at all. SanitizedGameJson holds the whole game
        // payload, and Update marks every column modified, so re-importing an idle game used to
        // rewrite that blob on every pass. CapturedAtUtc therefore records when the content last
        // changed, not when it was last polled.
        var participants = new List<PickupPalGameParticipant>(game.Participants.Count);
        foreach (var (participant, index) in game.Participants.Select((p, i) => (p, i)))
        {
            var profile = await ResolveOrCreateProfileAsync(participant, lookups, profileCache, cancellationToken);
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
        ImportLookups lookups,
        Dictionary<string, PlayerProfile> profileCache,
        CancellationToken cancellationToken)
    {
        var keys = BuildProfileCacheKeys(participant);
        if (keys.Count == 0)
        {
            // Deliberate: a participant carrying no user id, phone hash, or WhatsApp JID hash is left
            // unlinked rather than falling through to the unambiguous-display-name match below. The
            // games API never exposes a raw phone (IPickupPalUserClient resolves by digits, which we
            // do not have), so a name is the only signal left and a name alone is not evidence of
            // identity. These people surface on the Game Day roster with a Match / "Is this you?"
            // action instead, so a human decides. Do not "fix" this by reaching the name fallback.
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
            profile = lookups.ProfilesByPickupPalUserId.GetValueOrDefault(userId);
        }

        if (profile is null && participant.PhoneNumberHash is { } phoneHash)
        {
            profile = lookups.ProfilesByPhoneNumberHash.GetValueOrDefault(phoneHash);
            if (HasConflictingPickupPalUser(profile, participant.UserId))
            {
                profile = null;
            }
        }

        if (profile is null && participant.WhatsAppJidHash is { } jidHash)
        {
            profile = lookups.ProfilesByWhatsAppJidHash.GetValueOrDefault(jidHash);
            if (HasConflictingPickupPalUser(profile, participant.UserId))
            {
                profile = null;
            }
        }

        // Last resort: some participants arrive with no user id, no phone, and only an opaque
        // WhatsApp id that a signed-in profile never carries, so no key can ever match and the same
        // person ends up with a second profile. Fall back to an unambiguous display-name match -
        // shared names are excluded when the lookup is built, so this never merges two people.
        if (profile is null && !string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            profile = lookups.ProfilesByUnambiguousDisplayName.GetValueOrDefault(
                participant.DisplayName.Trim().ToUpperInvariant());
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

    // CapturedAtUtc is deliberately excluded: it is the only field that changes on every pass, so
    // including it would defeat the check and rewrite the payload blob each time.
    private static bool SnapshotMatches(
        PickupPalGameSnapshot snapshot,
        PickupPalGame game,
        Guid sessionId,
        string sanitizedJson) =>
        snapshot.SessionId == sessionId
        && snapshot.StartsAtUtc == game.StartsAtUtc
        && string.Equals(snapshot.Location, Truncate(game.Location, 512), StringComparison.Ordinal)
        && snapshot.MaxPlayers == game.MaxPlayers
        && string.Equals(snapshot.Status, Truncate(game.Status, 32), StringComparison.Ordinal)
        && string.Equals(snapshot.GroupName, Truncate(game.GroupName, 160), StringComparison.Ordinal)
        && string.Equals(snapshot.SanitizedGameJson, sanitizedJson, StringComparison.Ordinal);

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

    private async Task<Venue> ResolveOrCreateVenueAsync(
        string location,
        ImportLookups lookups,
        CancellationToken cancellationToken)
    {
        var name = ResolveVenueName(location);
        if (lookups.VenuesByName.TryGetValue(name, out var existing))
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
        // Register immediately: a venue added in this pass is not yet queryable, so without this a
        // second game at the same new location would insert a duplicate venue row.
        lookups.VenuesByName[name] = venue;
        return venue;
    }

    /// <summary>
    /// Reads every session, snapshot, venue, and player-profile row this pass can need, in a fixed
    /// number of batched queries rather than a per-game and per-participant lookup each.
    /// </summary>
    private async Task<ImportLookups> PrefetchAsync(
        IReadOnlyList<PickupPalGame> games,
        CancellationToken cancellationToken)
    {
        var lookups = new ImportLookups();

        // Deliberately the live, name-scoped read rather than the cached active list: a miss here
        // does not serve stale data, it INSERTS a venue. A second instance reading a cached list
        // would not see a venue this one just created and would duplicate the row. This also drops
        // the active list's 100-row cap, which silently broke resolution past 100 venues.
        var venueNames = games
            .Select(game => ResolveVenueName(game.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var venue in await venueRepository.ListByNamesAsync(venueNames, cancellationToken))
        {
            lookups.VenuesByName.TryAdd(venue.Name, venue);
        }

        var gameIds = games.Select(game => game.Id).Distinct(StringComparer.Ordinal).ToArray();
        var snapshots = await gameRepository.ListSnapshotsByGameIdsAsync(gameIds, cancellationToken);
        IndexByRequestedKey(
            gameIds,
            snapshots,
            snapshot => snapshot.PickupPalGameId,
            lookups.SnapshotsByGameId);

        var snapshotSessionIds = snapshots.Select(snapshot => snapshot.SessionId).Distinct().ToArray();
        foreach (var session in await sessionRepository.ListByIdsAsync(snapshotSessionIds, cancellationToken))
        {
            lookups.SessionsById.TryAdd(session.Id, session);
        }

        var occurrenceKeys = gameIds.Select(BuildOccurrenceKey).ToArray();
        IndexByRequestedKey(
            occurrenceKeys,
            await sessionRepository.ListByOccurrenceKeysAsync(occurrenceKeys, cancellationToken),
            session => session.OccurrenceKey,
            lookups.SessionsByOccurrenceKey);

        var participants = games.SelectMany(game => game.Participants).ToArray();

        var userIds = participants
            .Select(participant => participant.UserId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IndexByRequestedKey(
            userIds,
            await playerProfileRepository.ListByPickupPalUserIdsAsync(userIds, cancellationToken),
            profile => profile.PickupPalUserId,
            lookups.ProfilesByPickupPalUserId);

        var phoneHashes = participants
            .Select(participant => participant.PhoneNumberHash)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IndexByRequestedKey(
            phoneHashes,
            await playerProfileRepository.ListByPhoneNumberHashesAsync(phoneHashes, cancellationToken),
            profile => profile.PhoneNumberHash,
            lookups.ProfilesByPhoneNumberHash);

        var jidHashes = participants
            .Select(participant => participant.WhatsAppJidHash)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IndexByRequestedKey(
            jidHashes,
            await playerProfileRepository.ListByWhatsAppJidHashesAsync(jidHashes, cancellationToken),
            profile => profile.WhatsAppJidHash,
            lookups.ProfilesByWhatsAppJidHash);

        var displayNames = participants
            .Select(participant => participant.DisplayName)
            .Where(displayName => !string.IsNullOrWhiteSpace(displayName))
            .Select(displayName => displayName.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var profilesByName = await playerProfileRepository.ListByNormalizedDisplayNamesAsync(
            displayNames,
            cancellationToken);
        // Mirrors FindSingleByNormalizedDisplayNameAsync: a name shared by more than one profile
        // resolves to nobody, so a common nickname never links two different players. Matching is
        // per requested name for the reason described on IndexByRequestedKey.
        foreach (var requestedName in displayNames)
        {
            var matches = profilesByName
                .Where(profile => MatchesRequestedKey(profile.NormalizedDisplayName, requestedName))
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
            {
                lookups.ProfilesByUnambiguousDisplayName[requestedName] = matches[0];
            }
        }

        return lookups;
    }

    /// <summary>
    /// Indexes batch results by the key the caller asked for rather than the value SQL returned.
    /// The two are not interchangeable: SQL Server's <c>IN</c> comparison is case-insensitive and
    /// ignores trailing spaces under the default collation, so a row can come back from the query
    /// and still miss an exact-match dictionary keyed on its stored value. Keying on the request
    /// reproduces what the per-key lookups resolved. Sources are ordered oldest-first, so the first
    /// match wins exactly as <c>FirstOrDefault</c> did.
    /// </summary>
    private static void IndexByRequestedKey<T>(
        IReadOnlyList<string> requestedKeys,
        IReadOnlyList<T> rows,
        Func<T, string?> selectRowKey,
        Dictionary<string, T> destination)
    {
        foreach (var requestedKey in requestedKeys)
        {
            var match = rows.FirstOrDefault(row => MatchesRequestedKey(selectRowKey(row), requestedKey));
            if (match is not null)
            {
                destination[requestedKey] = match;
            }
        }
    }

    private static bool MatchesRequestedKey(string? rowKey, string requestedKey) =>
        rowKey is not null
        && string.Equals(rowKey.TrimEnd(), requestedKey.TrimEnd(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Row sets read once per import pass and resolved from memory thereafter.</summary>
    private sealed class ImportLookups
    {
        public Dictionary<string, Venue> VenuesByName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PickupPalGameSnapshot> SnapshotsByGameId { get; } = new(StringComparer.Ordinal);

        public Dictionary<Guid, Session> SessionsById { get; } = [];

        public Dictionary<string, Session> SessionsByOccurrenceKey { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PlayerProfile> ProfilesByPickupPalUserId { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PlayerProfile> ProfilesByPhoneNumberHash { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PlayerProfile> ProfilesByWhatsAppJidHash { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PlayerProfile> ProfilesByUnambiguousDisplayName { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    // Single definition of a game's venue name: the prefetch and the create-or-reuse decision must
    // derive it identically or the prefetch would miss and duplicate venues.
    private static string ResolveVenueName(string location) =>
        Truncate(string.IsNullOrWhiteSpace(location) ? "Pickup Pal venue" : location.Trim(), 160);

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
