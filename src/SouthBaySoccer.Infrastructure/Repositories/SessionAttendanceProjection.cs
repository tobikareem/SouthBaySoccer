using System;
using System.Collections.Generic;
using System.Linq;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal enum SessionAttendanceState
{
    Going,
    Waitlisted
}

internal enum SessionAttendanceSource
{
    Imported,
    Local
}

internal sealed record SessionAttendanceEntry(
    Guid SessionId,
    string IdentityKey,
    SessionAttendanceState State,
    SessionAttendanceSource Source);

internal sealed record SessionAttendanceSnapshot(
    IReadOnlySet<string> GoingKeys,
    IReadOnlySet<string> WaitlistKeys);

internal static class SessionAttendanceProjection
{
    internal static IReadOnlyDictionary<Guid, SessionAttendanceSnapshot> Build(
        IEnumerable<SessionAttendanceEntry> entries)
    {
        var statesBySession = new Dictionary<Guid, Dictionary<string, SessionAttendanceEntry>>();

        foreach (var entry in entries)
        {
            if (!statesBySession.TryGetValue(entry.SessionId, out var statesByIdentity))
            {
                statesByIdentity = new Dictionary<string, SessionAttendanceEntry>(StringComparer.Ordinal);
                statesBySession.Add(entry.SessionId, statesByIdentity);
            }

            if (!statesByIdentity.TryGetValue(entry.IdentityKey, out var current)
                || HasPrecedence(entry, current))
            {
                statesByIdentity[entry.IdentityKey] = entry;
            }
        }

        return statesBySession.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var going = pair.Value.Values
                    .Where(entry => entry.State == SessionAttendanceState.Going)
                    .Select(entry => entry.IdentityKey)
                    .ToHashSet(StringComparer.Ordinal);
                var waitlist = pair.Value.Values
                    .Where(entry => entry.State == SessionAttendanceState.Waitlisted)
                    .Select(entry => entry.IdentityKey)
                    .ToHashSet(StringComparer.Ordinal);
                return new SessionAttendanceSnapshot(going, waitlist);
            });
    }

    internal static SessionAttendanceSnapshot BuildForSession(
        Guid sessionId,
        IEnumerable<SessionAttendanceEntry> entries) =>
        Build(entries).TryGetValue(sessionId, out var snapshot)
            ? snapshot
            : new SessionAttendanceSnapshot(
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));

    internal static bool CanConfirm(
        SessionAttendanceSnapshot attendance,
        string playerIdentityKey,
        int capacity) =>
        attendance.GoingKeys.Contains(playerIdentityKey)
        || attendance.GoingKeys.Count < capacity;

    internal static string ProfileKey(Guid playerProfileId) => $"profile:{playerProfileId:N}";

    internal static string ParticipantKey(Guid? playerProfileId, string pickupPalParticipantId) =>
        playerProfileId is { } profileId
            ? ProfileKey(profileId)
            : $"pickuppal:{pickupPalParticipantId}";

    private static bool HasPrecedence(SessionAttendanceEntry candidate, SessionAttendanceEntry current) =>
        candidate.Source > current.Source
        || candidate.Source == current.Source
            && candidate.State == SessionAttendanceState.Going
            && current.State == SessionAttendanceState.Waitlisted;
}
