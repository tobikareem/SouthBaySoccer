using SouthBaySoccer.Domain.Entities.Scheduling;

namespace SouthBaySoccer.Application.Features.Scheduling;

internal static class SchedulingMappers
{
    public static SeasonModel ToModel(Season season) =>
        new(season.Id, season.Name, season.StartsAtUtc, season.EndsAtUtc);

    public static VenueModel ToModel(Venue venue) =>
        new(venue.Id, venue.Name, venue.Locality, venue.Address, venue.MapsProviderReference);

    public static SessionModel ToModel(Session session) =>
        new(
            session.Id,
            session.SeasonId,
            session.VenueId,
            session.RecurrenceRuleId,
            session.Title,
            session.Format,
            session.Capacity,
            session.TeamCount,
            session.StartsAtUtc,
            session.CheckInOpensAtUtc,
            session.CheckInClosesAtUtc,
            session.RsvpDeadlineUtc,
            session.OccurrenceKey,
            session.Status.ToString());

    public static RecurrenceRuleModel ToModel(RecurrenceRule recurrenceRule) =>
        new(recurrenceRule.Id, recurrenceRule.Name, recurrenceRule.TimeZoneId, recurrenceRule.Rule);
}
