using FluentAssertions;
using SouthBaySoccer.Infrastructure.Repositories;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class SessionAttendanceProjectionTests
{
    [Fact]
    public void BuildForSession_WhenLocalWaitlistConflictsWithImportedGoing_LocalWaitlistWins()
    {
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var key = SessionAttendanceProjection.ProfileKey(playerId);
        var entries = new[]
        {
            new SessionAttendanceEntry(
                sessionId,
                key,
                SessionAttendanceState.Going,
                SessionAttendanceSource.Imported),
            new SessionAttendanceEntry(
                sessionId,
                key,
                SessionAttendanceState.Waitlisted,
                SessionAttendanceSource.Local)
        };

        var result = SessionAttendanceProjection.BuildForSession(sessionId, entries);

        result.GoingKeys.Should().BeEmpty();
        result.WaitlistKeys.Should().BeEquivalentTo(key);
    }

    [Fact]
    public void BuildForSession_WhenLocalGoingConflictsWithImportedWaitlist_LocalGoingWins()
    {
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var key = SessionAttendanceProjection.ProfileKey(playerId);
        var entries = new[]
        {
            new SessionAttendanceEntry(
                sessionId,
                key,
                SessionAttendanceState.Waitlisted,
                SessionAttendanceSource.Imported),
            new SessionAttendanceEntry(
                sessionId,
                key,
                SessionAttendanceState.Going,
                SessionAttendanceSource.Local)
        };

        var result = SessionAttendanceProjection.BuildForSession(sessionId, entries);

        result.GoingKeys.Should().BeEquivalentTo(key);
        result.WaitlistKeys.Should().BeEmpty();
    }

    [Fact]
    public void BuildForSession_WhenImportedIdentityAppearsInBothStates_GoingWins()
    {
        var sessionId = Guid.NewGuid();
        const string key = "pickuppal:participant-1";
        var entries = new[]
        {
            new SessionAttendanceEntry(
                sessionId,
                key,
                SessionAttendanceState.Waitlisted,
                SessionAttendanceSource.Imported),
            new SessionAttendanceEntry(
                sessionId,
                key,
                SessionAttendanceState.Going,
                SessionAttendanceSource.Imported)
        };

        var result = SessionAttendanceProjection.BuildForSession(sessionId, entries);

        result.GoingKeys.Should().BeEquivalentTo(key);
        result.WaitlistKeys.Should().BeEmpty();
    }

    [Fact]
    public void CanConfirm_WhenImportedRosterFillsCapacity_RejectsNewGoingPlayer()
    {
        var sessionId = Guid.NewGuid();
        var importedKey = "pickuppal:participant-1";
        var attendance = SessionAttendanceProjection.BuildForSession(
            sessionId,
            new[]
            {
                new SessionAttendanceEntry(
                    sessionId,
                    importedKey,
                    SessionAttendanceState.Going,
                    SessionAttendanceSource.Imported)
            });

        var canConfirm = SessionAttendanceProjection.CanConfirm(
            attendance,
            SessionAttendanceProjection.ProfileKey(Guid.NewGuid()),
            capacity: 1);

        canConfirm.Should().BeFalse();
    }
}
