using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed class CreateSessionCommandHandler(
    IValidator<CreateSessionCommand> validator,
    ISeasonRepository seasonRepository,
    IVenueRepository venueRepository,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<SessionModel> HandleAsync(CreateSessionCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        await EnsureParentsExistAsync(command.SeasonId, command.VenueId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(command.OccurrenceKey))
        {
            var existing = await sessionRepository.FindByOccurrenceKeyAsync(command.OccurrenceKey, cancellationToken);
            if (existing is not null)
            {
                return SchedulingMappers.ToModel(existing);
            }
        }

        await EnsureNotDuplicateAsync(
            sessionRepository, command.VenueId, command.Title, command.StartsAtUtc, cancellationToken);

        var session = CreateSession(command);
        await sessionRepository.AddAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(session);
    }

    private async Task EnsureParentsExistAsync(Guid seasonId, Guid venueId, CancellationToken cancellationToken)
    {
        if (await seasonRepository.GetByIdAsync(seasonId, cancellationToken) is null)
        {
            throw new ApplicationNotFoundException("Season was not found.");
        }

        if (await venueRepository.GetByIdAsync(venueId, cancellationToken) is null)
        {
            throw new ApplicationNotFoundException("Venue was not found.");
        }
    }

    internal static async Task EnsureNotDuplicateAsync(
        ISessionRepository sessionRepository,
        Guid venueId,
        string title,
        DateTime startsAtUtc,
        CancellationToken cancellationToken)
    {
        if (await sessionRepository.ExistsDuplicateAsync(venueId, title.Trim(), startsAtUtc, cancellationToken))
        {
            throw new ApplicationConflictException(
                "A session with the same venue, name, and start time already exists.");
        }
    }

    private static Session CreateSession(CreateSessionCommand command) =>
        new()
        {
            Id = Guid.NewGuid(),
            SeasonId = command.SeasonId,
            VenueId = command.VenueId,
            RecurrenceRuleId = command.RecurrenceRuleId,
            Title = command.Title.Trim(),
            Format = command.Format.Trim(),
            Capacity = command.Capacity,
            TeamCount = command.TeamCount,
            StartsAtUtc = command.StartsAtUtc,
            CheckInOpensAtUtc = command.CheckInOpensAtUtc,
            CheckInClosesAtUtc = command.CheckInClosesAtUtc,
            RsvpDeadlineUtc = command.RsvpDeadlineUtc,
            OccurrenceKey = string.IsNullOrWhiteSpace(command.OccurrenceKey) ? null : command.OccurrenceKey.Trim(),
            Status = command.Status,
        };
}

public sealed class ListUpcomingSessionsQueryHandler(
    ICurrentUser currentUser,
    SouthBaySoccer.Application.Abstractions.Time.IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository)
{
    public async Task<IReadOnlyList<SessionFeedModel>> HandleAsync(
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var boundedTake = Math.Clamp(take, 1, 100);
        var sessions = await sessionRepository.ListUpcomingFeedAsync(
            clock.UtcNow,
            boundedTake,
            profile.Id,
            cancellationToken);

        return sessions.Select(record =>
        {
            var isFull = record.GoingCount >= record.Session.Capacity;
            var canJoinWaitlist = record.Session.Status == SessionStatus.Published
                && clock.UtcNow < record.Session.RsvpDeadlineUtc
                && isFull
                && !record.IsCurrentPlayerGoing
                && !record.IsCurrentPlayerWaitlisted;
            return new SessionFeedModel(
                SchedulingMappers.ToModel(record.Session),
                record.VenueName,
                record.GoingCount,
                record.WaitlistCount,
                isFull,
                record.IsCurrentPlayerGoing,
                record.IsCurrentPlayerWaitlisted,
                canJoinWaitlist,
                record.GroupName);
        }).ToArray();
    }
}

public sealed class CancelSessionCommandHandler(
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<SessionModel> HandleAsync(CancelSessionCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ApplicationConflictException("Cancellation reason is required.");
        }

        var session = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        session.Status = SessionStatus.Canceled;
        sessionRepository.Update(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(session);
    }
}

public sealed class DeleteSessionCommandHandler(
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteSessionCommand command, CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        sessionRepository.SoftDelete(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CreateRecurrenceRuleCommandHandler(
    IValidator<CreateRecurrenceRuleCommand> validator,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<RecurrenceRuleModel> HandleAsync(
        CreateRecurrenceRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var recurrenceRule = new RecurrenceRule
        {
            Id = Guid.NewGuid(),
            Name = command.Name.Trim(),
            TimeZoneId = command.TimeZoneId.Trim(),
            Rule = command.Rule.Trim(),
        };

        await sessionRepository.AddRecurrenceRuleAsync(recurrenceRule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SchedulingMappers.ToModel(recurrenceRule);
    }
}

public sealed class CreateSessionOccurrenceCommandHandler(
    IValidator<CreateSessionOccurrenceCommand> validator,
    CreateSessionCommandHandler createSessionHandler,
    ISessionRepository sessionRepository)
{
    public async Task<SessionModel> HandleAsync(
        CreateSessionOccurrenceCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (await sessionRepository.FindRecurrenceRuleAsync(command.RecurrenceRuleId, cancellationToken) is null)
        {
            throw new ApplicationNotFoundException("Recurrence rule was not found.");
        }

        var occurrenceKey = BuildOccurrenceKey(command.RecurrenceRuleId, command.OccurrenceStartsAtUtc);
        var existing = await sessionRepository.FindByOccurrenceKeyAsync(occurrenceKey, cancellationToken);
        if (existing is not null)
        {
            return SchedulingMappers.ToModel(existing);
        }

        return await createSessionHandler.HandleAsync(
            new CreateSessionCommand(
                command.SeasonId,
                command.VenueId,
                command.Title,
                command.Format,
                command.Capacity,
                command.TeamCount,
                command.OccurrenceStartsAtUtc,
                command.CheckInOpensAtUtc,
                command.CheckInClosesAtUtc,
                command.RsvpDeadlineUtc,
                command.RecurrenceRuleId,
                occurrenceKey),
            cancellationToken);
    }

    private static string BuildOccurrenceKey(Guid recurrenceRuleId, DateTime occurrenceStartsAtUtc) =>
        $"{recurrenceRuleId:N}:{occurrenceStartsAtUtc:yyyyMMddTHHmmssZ}";
}
