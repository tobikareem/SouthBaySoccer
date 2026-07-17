using FluentAssertions;
using SouthBaySoccer.Application.Features.Scheduling;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class SessionAdminTimeZoneTests
{
    [Fact]
    public void ToUtc_ThenToLocal_RoundTripsToTheOriginalLocalDateAndTime()
    {
        var localDate = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Unspecified);
        var localTime = new TimeSpan(19, 40, 0);

        var utc = SessionAdminTimeZone.ToUtc(localDate, localTime);
        var local = SessionAdminTimeZone.ToLocal(utc);

        utc.Kind.Should().Be(DateTimeKind.Utc);
        local.Date.Should().Be(localDate.Date);
        local.TimeOfDay.Should().Be(localTime);
    }

    [Fact]
    public void ToUtc_WhenLocalTimeIsNonexistentDueToSpringForward_AdjustsForwardOneHourInsteadOfThrowing()
    {
        // Pacific clocks spring forward from 2:00 AM to 3:00 AM on 2026-03-08; 2:30 AM that day does
        // not exist as a local time.
        var localDate = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Unspecified);
        var nonexistentLocalTime = new TimeSpan(2, 30, 0);

        var act = () => SessionAdminTimeZone.ToUtc(localDate, nonexistentLocalTime);

        act.Should().NotThrow();
        var utc = act();
        // Shifted forward one hour to 3:30 AM PDT (UTC-7) == 10:30 AM UTC.
        utc.Should().Be(new DateTime(2026, 3, 8, 10, 30, 0, DateTimeKind.Utc));
    }
}
