using FluentValidation;
using Microsoft.Extensions.Logging;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class GetCreateSessionAdminDefaultsQueryHandler(
    IClock clock,
    IVenueRepository venueRepository,
    ImportPickupPalGamesCommandHandler importPickupPalGamesHandler,
    ILogger<GetCreateSessionAdminDefaultsQueryHandler> logger)
{
    private static readonly string[] Formats = ["5v5", "7v7", "9v9"];
    private static readonly string[] TeamOptions = ["2 teams", "3 teams", "4 teams"];
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromSeconds(5);

    public async Task<CreateSessionAdminDefaultsModel> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        // Opening Create session refreshes reality from Pickup Pal first (source of truth for
        // imported games). The import is fail-open and time-boxed: if Pickup Pal is unreachable or
        // slow, the admin still gets defaults built from local data instead of blocking the screen.
        using var importCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        importCts.CancelAfter(ImportTimeout);
        try
        {
            var import = await importPickupPalGamesHandler.HandleAsync(importCts.Token);
            if (import.Warnings.Count > 0)
            {
                logger.LogWarning(
                    "Pickup Pal import finished with {WarningCount} warning(s): {Warnings}",
                    import.Warnings.Count,
                    string.Join("; ", import.Warnings));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Pickup Pal import timed out after {TimeoutSeconds}s; loading Create session with local data.",
                ImportTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            // Fail-open by design; the import is advisory and the screen must still load.
            logger.LogWarning(ex, "Pickup Pal import failed; loading Create session with local data.");
        }

        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        var savedVenue = venues.FirstOrDefault();
        var localToday = SessionAdminTimeZone.ToLocal(clock.UtcNow).Date;
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
}

public sealed class ListManagedSessionsQueryHandler(
    IClock clock,
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository,
    IGroupChatRepository groupChatRepository)
{
    public async Task<IReadOnlyList<ManagedSessionModel>> HandleAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var sessions = await sessionRepository.ListManagedAsync(clock.UtcNow, Math.Clamp(take, 1, 100), cancellationToken);
        var venues = await venueRepository.ListActiveAsync(cancellationToken);
        var venueNames = venues.ToDictionary(x => x.Id, x => x.Name);
        var groupIds = sessions.Select(x => x.GroupChatId).OfType<Guid>().Distinct().ToArray();
        var groupNames = (await groupChatRepository.ListByIdsAsync(groupIds, cancellationToken))
            .ToDictionary(x => x.Id, x => x.GroupName);

        return sessions
            .Select(session => new ManagedSessionModel(
                session.Id,
                session.Title,
                session.StartsAtUtc,
                venueNames.TryGetValue(session.VenueId, out var venueName) ? venueName : "Unknown venue",
                session.Format,
                session.Capacity,
                session.Status.ToString(),
                session.GroupChatId,
                session.GroupChatId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null))
            .ToArray();
    }
}

public sealed class GetSessionForAdminEditQueryHandler(
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository,
    SessionGroupResolver groupResolver)
{
    public async Task<ManagedSessionEditModel> HandleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);
        var group = await groupResolver.FindAsync(session.GroupChatId, cancellationToken);

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
            session.Status.ToString(),
            session.GroupChatId,
            group?.GroupName);
    }
}

public sealed class CreateSessionDraftCommandHandler(
    IValidator<CreateSessionCommand> validator,
    ISeasonRepository seasonRepository,
    IVenueRepository venueRepository,
    ISessionRepository sessionRepository,
    SessionGroupResolver groupResolver,
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
        await CreateSessionCommandHandler.EnsureNotDuplicateAsync(
            sessionRepository,
            createCommand.VenueId,
            createCommand.Title,
            createCommand.StartsAtUtc,
            cancellationToken);
        // Drafts carry their group from the start so Publish can create the Pickup Pal game
        // without a second round trip; drafts themselves never push.
        var group = await groupResolver.ResolveAsync(command.GroupChatId, cancellationToken);
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
            GroupChatId = group?.Id,
        };

        await sessionRepository.AddAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(session, group);
    }

    internal static CreateSessionCommand ToCreateSessionCommand(
        CreateSessionDraftCommand command,
        Guid seasonId,
        Guid venueId,
        SessionStatus status) =>
        new(
            seasonId,
            venueId,
            $"{command.VenueName.Trim()} - {WeekdayPickupTitle(command.StartsAtUtc)}",
            command.Format,
            command.Capacity,
            command.TeamCount,
            command.StartsAtUtc,
            command.CheckInOpensAtUtc,
            command.CheckInClosesAtUtc,
            command.RsvpDeadlineUtc,
            Status: status,
            GroupChatId: command.GroupChatId);

    /// <summary>Derives "&lt;Weekday&gt; pickup" from the session's venue-local start date.</summary>
    private static string WeekdayPickupTitle(DateTime startsAtUtc) =>
        $"{SessionAdminTimeZone.ToLocal(startsAtUtc).DayOfWeek} pickup";

    internal static async Task<Season> ResolveSeasonAsync(
        ISeasonRepository seasonRepository,
        DateTime startsAtUtc,
        CancellationToken cancellationToken)
    {
        var seasons = await seasonRepository.ListActiveAsync(cancellationToken);
        return seasons.FirstOrDefault(x => x.StartsAtUtc <= startsAtUtc && x.EndsAtUtc >= startsAtUtc)
            ?? throw new ApplicationConflictException("No season covers the session start date.");
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
    SessionGroupResolver groupResolver,
    ISessionPickupPalSyncService pickupPalSyncService,
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
                command.RsvpDeadlineUtc,
                command.GroupChatId),
            season.Id,
            venue.Id,
            session.Status);

        await validator.ValidateAndThrowAsync(createCommand, cancellationToken);

        // An explicit group replaces the association; a missing one keeps what the session already
        // has (the current client never sends it, so a group can never be cleared), and only an
        // unlinked session falls back to the admin's single group. Once the Pickup Pal game exists
        // the group is fixed: the game lives in that WhatsApp group.
        if (command.GroupChatId is { } requestedGroupId
            && !string.IsNullOrWhiteSpace(session.PickupPalGameId)
            && requestedGroupId != session.GroupChatId)
        {
            throw new ApplicationConflictException("The group cannot change once the Pickup Pal game exists.");
        }

        var group = command.GroupChatId is not null || session.GroupChatId is null
            ? await groupResolver.ResolveAsync(command.GroupChatId, cancellationToken)
            : await groupResolver.FindAsync(session.GroupChatId, cancellationToken);

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
        session.GroupChatId = group?.Id ?? session.GroupChatId;

        sessionRepository.Update(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Local first: a published session with an app-created game pushes location, capacity,
        // and start to Pickup Pal; drafts and imported games are NotApplicable inside the service.
        await pickupPalSyncService.SyncAfterLocalWriteAsync(session.Id, cancellationToken);
        return SchedulingMappers.ToModel(session, group);
    }
}

public sealed class PublishSessionCommandHandler(
    ISessionRepository sessionRepository,
    SessionGroupResolver groupResolver,
    ISessionPickupPalSyncService pickupPalSyncService,
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

        // Local first: the publish is committed, then the Pickup Pal game is created for a session
        // with a group (app-only sessions stay app-only; failures leave a pending outbox row).
        await pickupPalSyncService.SyncAfterLocalWriteAsync(session.Id, cancellationToken);
        var group = await groupResolver.FindAsync(session.GroupChatId, cancellationToken);
        return SchedulingMappers.ToModel(session, group);
    }
}
