using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

public sealed class SubmitRsvpCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<SubmitRsvpCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IPlayerSessionEligibilityService eligibilityService,
    IRsvpRepository rsvpRepository)
{
    public async Task<RsvpResultModel> HandleAsync(SubmitRsvpCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var profile = await GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var session = await GetOpenSessionAsync(sessionRepository, command.SessionId, clock.UtcNow, cancellationToken);

        var eligibility = await eligibilityService.CheckAsync(profile.Id, session.Id, cancellationToken);
        if (!eligibility.IsEligible)
        {
            throw new ApplicationConflictException(eligibility.Reason ?? "Player is not eligible to RSVP.");
        }

        var result = await rsvpRepository.SubmitRsvpAsync(session.Id, profile.Id, command.Status, cancellationToken);
        return RsvpMapper.ToModel(result);
    }

    internal static async Task<SouthBaySoccer.Domain.Entities.Identity.PlayerProfile> GetCurrentProfileAsync(
        ICurrentUser currentUser,
        IPlayerProfileRepository playerProfileRepository,
        CancellationToken cancellationToken)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        return await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
    }

    internal static async Task<Session> GetOpenSessionAsync(
        ISessionRepository sessionRepository,
        Guid sessionId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        if (session.Status != SessionStatus.Published)
        {
            throw new ApplicationConflictException("RSVP is not available for this session.");
        }

        if (session.RsvpDeadlineUtc <= nowUtc)
        {
            throw new ApplicationConflictException("RSVP deadline has passed.");
        }

        return session;
    }
}

public sealed class CancelRsvpCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IPlayerSessionEligibilityService eligibilityService,
    IRsvpRepository rsvpRepository)
{
    public async Task<RsvpResultModel> HandleAsync(CancelRsvpCommand command, CancellationToken cancellationToken = default)
    {
        var profile = await SubmitRsvpCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var session = await SubmitRsvpCommandHandler.GetOpenSessionAsync(sessionRepository, command.SessionId, clock.UtcNow, cancellationToken);

        var result = await rsvpRepository.CancelAndPromoteAsync(
            session.Id,
            profile.Id,
            async (candidatePlayerProfileId, token) =>
                (await eligibilityService.CheckAsync(candidatePlayerProfileId, session.Id, token)).IsEligible,
            cancellationToken);

        return RsvpMapper.ToModel(result);
    }
}

public sealed class GetMyRsvpQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IRsvpRepository rsvpRepository)
{
    public async Task<RsvpResultModel?> HandleAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var profile = await SubmitRsvpCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var result = await rsvpRepository.GetMyRsvpAsync(sessionId, profile.Id, cancellationToken);
        return result is null ? null : RsvpMapper.ToModel(result);
    }
}

public sealed class AdminOverrideRsvpCommandHandler(
    ICurrentUser currentUser,
    IValidator<AdminOverrideRsvpCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository)
{
    public async Task<RsvpResultModel> HandleAsync(AdminOverrideRsvpCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var adminProfile = await SubmitRsvpCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        _ = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");
        _ = await playerProfileRepository.FindProfileAsync(command.PlayerProfileId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        var result = await rsvpRepository.AddWithAdminOverrideAsync(
            command.SessionId,
            command.PlayerProfileId,
            adminProfile.Id,
            command.Reason,
            cancellationToken);

        return RsvpMapper.ToModel(result);
    }
}

public sealed class CheckInPlayerCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<CheckInPlayerCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository)
{
    public async Task<CheckInResultModel> HandleAsync(CheckInPlayerCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var adminProfile = await SubmitRsvpCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var session = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");
        var nowUtc = clock.UtcNow;
        var lateOverrideReason = string.IsNullOrWhiteSpace(command.LateOverrideReason)
            ? null
            : command.LateOverrideReason.Trim();

        var isOutsideCheckInWindow = nowUtc < session.CheckInOpensAtUtc || nowUtc > session.CheckInClosesAtUtc;
        if (isOutsideCheckInWindow && lateOverrideReason is null)
        {
            throw new ApplicationConflictException("Check-in is outside the session check-in window and requires a late override reason.");
        }

        var effectiveLateOverrideReason = isOutsideCheckInWindow ? lateOverrideReason : null;

        var result = await rsvpRepository.RecordCheckInAsync(
            command.SessionId,
            command.PlayerProfileId,
            adminProfile.Id,
            nowUtc,
            command.Outcome,
            effectiveLateOverrideReason,
            cancellationToken);
        var checkIn = result.CheckIn;

        return new CheckInResultModel(
            checkIn.Id,
            checkIn.SessionId,
            checkIn.PlayerProfileId,
            checkIn.CheckedInByPlayerProfileId!.Value,
            checkIn.CheckedInAtUtc,
            checkIn.Outcome.ToString(),
            result.AdminOverrideId.HasValue,
            result.AdminOverrideId,
            result.LateOverrideReason);
    }
}

public sealed class RecordNoShowsCommandHandler(
    IClock clock,
    IRsvpRepository rsvpRepository)
{
    public async Task<NoShowResultModel> HandleAsync(RecordNoShowsCommand command, CancellationToken cancellationToken = default)
    {
        var count = await rsvpRepository.RecordNoShowsAsync(command.SessionId, clock.UtcNow, cancellationToken);
        return new NoShowResultModel(command.SessionId, count);
    }
}
