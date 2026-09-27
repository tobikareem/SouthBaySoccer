namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>
/// Shared Pacific-time conversion helpers for the session admin workflow. Both the Application layer
/// (<see cref="SessionAdminWorkflowHandlers"/>) and the Functions layer (<c>SchedulingFunctions</c>)
/// convert between the venue-local time the admin form edits and the UTC timestamps stored on
/// <see cref="Domain.Entities.Scheduling.Session"/>; this is the single home for that conversion so the
/// two call sites cannot drift out of sync. Functions is allowed to depend on Application, so this is
/// the correct layer per the dependency rule.
/// </summary>
public static class SessionAdminTimeZone
{
    /// <summary>The IANA zone used when a WhatsApp group does not report one (the South Bay default).</summary>
    public const string DefaultTimeZoneId = "America/Los_Angeles";

    public static TimeZoneInfo Pacific { get; } = FindPacificTimeZone();

    /// <summary>
    /// Resolves a group's IANA time zone id, falling back to <see cref="Pacific"/> when the id is
    /// missing or unknown on this host. Used to express a session's UTC start in the group's local
    /// date and time when the game is pushed to Pickup Pal.
    /// </summary>
    public static TimeZoneInfo Resolve(string? ianaTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZoneId))
        {
            return Pacific;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return Pacific;
        }
        catch (InvalidTimeZoneException)
        {
            return Pacific;
        }
    }

    /// <summary>Converts a UTC instant to local time in <paramref name="zone"/>.</summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(normalized, zone);
    }

    /// <summary>
    /// Combines a local calendar date and time-of-day into the equivalent UTC instant.
    /// </summary>
    /// <remarks>
    /// The DST spring-forward transition removes an hour from the local calendar (e.g. Pacific clocks
    /// skip from 2:00 AM directly to 3:00 AM), which makes some local date/time combinations
    /// nonexistent. <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> throws
    /// <see cref="ArgumentException"/> for those. Rather than let a session save fail outright for
    /// admins who happen to land on the transition, an invalid local time is shifted forward by one
    /// hour into the first valid moment after the transition.
    /// </remarks>
    public static DateTime ToUtc(DateTime localDate, TimeSpan localTime)
    {
        var local = DateTime.SpecifyKind(localDate.Date + localTime, DateTimeKind.Unspecified);
        if (Pacific.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, Pacific);
    }

    public static DateTime ToLocal(DateTime utc) => ToLocal(utc, Pacific);

    private static TimeZoneInfo FindPacificTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
    }
}
