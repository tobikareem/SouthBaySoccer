using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class GetCreateSessionAdminDefaultsQueryHandler(
    IClock clock,
    IVenueRepository venueRepository)
{
    private static readonly string[] Formats = ["5v5", "7v7", "9v9"];
    private static readonly string[] TeamOptions = ["2 teams", "3 teams", "4 teams"];

    public async Task<CreateSessionAdminDefaultsModel> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        var savedVenue = venues.FirstOrDefault();
        var localToday = ToPacificLocal(clock.UtcNow).Date;
        var defaultDate = NextSaturday(localToday);
        var defaultStart = new TimeSpan(19, 40, 0);

        return new CreateSessionAdminDefaultsModel(
            true,
            defaultDate,
            defaultStart,
            10,
            0,
            Formats,
            1,
            20,
            1,
            40,
            TeamOptions,
            0,
            savedVenue is null
                ? new VenueModel(Guid.Empty, string.Empty, string.Empty, null, null)
                : SchedulingMappers.ToModel(savedVenue),
            "Team feed",
            new TimeSpan(18, 30, 0));
    }

    private static DateTime NextSaturday(DateTime localToday)
    {
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)localToday.DayOfWeek + 7) % 7;
        if (daysUntilSaturday == 0)
        {
            daysUntilSaturday = 7;
        }

        return DateTime.SpecifyKind(localToday.AddDays(daysUntilSaturday), DateTimeKind.Unspecified);
    }

    private static DateTime ToPacificLocal(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Utc
            ? utcNow
            : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, SessionAdminTimeZone.Pacific);
    }
}

public sealed class ListManagedSessionsQueryHandler(
    IClock clock,
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository)
{
    public async Task<IReadOnlyList<ManagedSessionModel>> HandleAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var sessions = await sessionRepository.ListManagedAsync(clock.UtcNow, Math.Clamp(take, 1, 100), cancellationToken);
        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        var venueNames = venues.ToDictionary(x => x.Id, x => x.Name);

        return sessions
            .Select(session => new ManagedSessionModel(
                session.Id,
                session.Title,
                session.StartsAtUtc,
                venueNames.TryGetValue(session.VenueId, out var venueName) ? venueName : "Unknown venue",
                session.Format,
                session.Capacity,
                session.Status.ToString()))
            .ToArray();
    }
}

public sealed class GetSessionForAdminEditQueryHandler(
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository)
{
    public async Task<ManagedSessionEditModel?> HandleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);

        return new ManagedSessionEditModel(
            session.Id,
            session.VenueId,
            venue?.Name ?? "Unknown venue",
            session.Format,
            session.Capacity,
            session.TeamCount,
            session.StartsAtUtc,
            session.CheckInOpensAtUtc,
            session.CheckInClosesAtUtc,
            session.RsvpDeadlineUtc,
            session.Status.ToString());
    }
}

public sealed class CreateSessionDraftCommandHandler(
    IValidator<CreateSessionCommand> validator,
    ISeasonRepository seasonRepository,
    IVenueRepository venueRepository,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<SessionModel> HandleAsync(
        CreateSessionDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var season = await ResolveSeasonAsync(seasonRepository, command.StartsAtUtc, cancellationToken);
        var venue = await ResolveVenueAsync(venueRepository, command.VenueId, command.VenueName, cancellationToken);
        var createCommand = ToCreateSessionCommand(command, season.Id, venue.Id, SessionStatus.Draft);

        await validator.ValidateAndThrowAsync(createCommand, cancellationToken);
        var session = new Session
        {
            Id = Guid.NewGuid(),
            SeasonId = createCommand.SeasonId,
            VenueId = createCommand.VenueId,
            RecurrenceRuleId = createCommand.RecurrenceRuleId,
            Title = createCommand.Title.Trim(),
            Format = createCommand.Format.Trim(),
            Capacity = createCommand.Capacity,
            TeamCount = createCommand.TeamCount,
            StartsAtUtc = createCommand.StartsAtUtc,
            CheckInOpensAtUtc = createCommand.CheckInOpensAtUtc,
            CheckInClosesAtUtc = createCommand.CheckInClosesAtUtc,
            RsvpDeadlineUtc = createCommand.RsvpDeadlineUtc,
            OccurrenceKey = null,
            Status = SessionStatus.Draft,
        };

        await sessionRepository.AddAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(session);
    }

    internal static CreateSessionCommand ToCreateSessionCommand(
        CreateSessionDraftCommand command,
        Guid seasonId,
        Guid venueId,
        SessionStatus status) =>
        new(
            seasonId,
            venueId,
            $"{command.VenueName.Trim()} - Saturday pickup",
            command.Format,
            command.Capacity,
            command.TeamCount,
            command.StartsAtUtc,
            command.CheckInOpensAtUtc,
            command.CheckInClosesAtUtc,
            command.RsvpDeadlineUtc,
            Status: status);

    internal static async Task<Season> ResolveSeasonAsync(
        ISeasonRepository seasonRepository,
        DateTime startsAtUtc,
        CancellationToken cancellationToken)
    {
        var seasons = await seasonRepository.ListActiveAsync(cancellationToken);
        return seasons.FirstOrDefault(x => x.StartsAtUtc <= startsAtUtc && x.EndsAtUtc >= startsAtUtc)
            ?? seasons.FirstOrDefault()
            ?? throw new ApplicationConflictException("A season is required before creating sessions.");
    }

    internal static async Task<Venue> ResolveVenueAsync(
        IVenueRepository venueRepository,
        Guid? venueId,
        string venueName,
        CancellationToken cancellationToken)
    {
        if (venueId is { } id && id != Guid.Empty)
        {
            return await venueRepository.GetByIdAsync(id, cancellationToken)
                ?? throw new ApplicationNotFoundException("Venue was not found.");
        }

        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        return venues.FirstOrDefault(x => string.Equals(x.Name, venueName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ApplicationConflictException("Choose a saved venue before creating the session.");
    }
}

public sealed class UpdateSessionAdminCommandHandler(
    IValidator<CreateSessionCommand> validator,
    ISeasonRepository seasonRepository,
    IVenueRepository venueRepository,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<SessionModel> HandleAsync(
        UpdateSessionAdminCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");
        if (session.Status == SessionStatus.Canceled || session.Status == SessionStatus.Completed)
        {
            throw new ApplicationConflictException("Canceled or completed sessions cannot be updated.");
        }

        var season = await CreateSessionDraftCommandHandler.ResolveSeasonAsync(seasonRepository, command.StartsAtUtc, cancellationToken);
        var venue = await CreateSessionDraftCommandHandler.ResolveVenueAsync(venueRepository, command.VenueId, command.VenueName, cancellationToken);
        var createCommand = CreateSessionDraftCommandHandler.ToCreateSessionCommand(
            new CreateSessionDraftCommand(
                command.VenueId,
                command.VenueName,
                command.Format,
                command.Capacity,
                command.TeamCount,
                command.StartsAtUtc,
                command.CheckInOpensAtUtc,
                command.CheckInClosesAtUtc,
                command.RsvpDeadlineUtc),
            season.Id,
            venue.Id,
            session.Status);

        await validator.ValidateAndThrowAsync(createCommand, cancellationToken);

        session.SeasonId = createCommand.SeasonId;
        session.VenueId = createCommand.VenueId;
        session.Title = createCommand.Title.Trim();
        session.Format = createCommand.Format.Trim();
        session.Capacity = createCommand.Capacity;
        session.TeamCount = createCommand.TeamCount;
        session.StartsAtUtc = createCommand.StartsAtUtc;
        session.CheckInOpensAtUtc = createCommand.CheckInOpensAtUtc;
        session.CheckInClosesAtUtc = createCommand.CheckInClosesAtUtc;
        session.RsvpDeadlineUtc = createCommand.RsvpDeadlineUtc;

        sessionRepository.Update(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(session);
    }
}

public sealed class PublishSessionCommandHandler(
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<SessionModel> HandleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        if (session.Status == SessionStatus.Published)
        {
            return SchedulingMappers.ToModel(session);
        }

        if (session.Status != SessionStatus.Draft)
        {
            throw new ApplicationConflictException("Only draft sessions can be published.");
        }

        session.Status = SessionStatus.Published;
        sessionRepository.Update(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(session);
    }
}

internal static class SessionAdminTimeZone
{
    public static TimeZoneInfo Pacific { get; } = FindPacificTimeZone();

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

