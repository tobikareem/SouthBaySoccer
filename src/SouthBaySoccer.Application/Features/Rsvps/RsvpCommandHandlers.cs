using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Groups;
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
    IRsvpRepository rsvpRepository,
    IRsvpPickupPalSyncService pickupPalSyncService,
    IGroupMembershipGate groupMembershipGate)
{
    public async Task<RsvpResultModel> HandleAsync(SubmitRsvpCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var profile = await GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var session = await GetOpenSessionAsync(sessionRepository, command.SessionId, clock.UtcNow, cancellationToken);

        // GRP-1: Going (and the waitlist it may land on) is reserved for approved members of the
        // session's group; a session without a group is open as before. Maybe / NotGoing and
        // cancel are never gated, so a removed member can always step back.
        if (command.Status == RsvpStatus.Going)
        {
            await groupMembershipGate.EnsureCanJoinAsync(session, profile.Id, cancellationToken);
        }

        var eligibility = await eligibilityService.CheckAsync(profile.Id, session.Id, cancellationToken);
        if (!eligibility.IsEligible)
        {
            throw new ApplicationConflictException(eligibility.Reason ?? "Player is not eligible to RSVP.");
        }

        var result = await rsvpRepository.SubmitRsvpAsync(session.Id, profile.Id, command.Status, cancellationToken);
        // Runs after the local transaction committed and never fails the RSVP (RSVP-9).
        var pickupPalSync = await pickupPalSyncService.SyncAfterLocalWriteAsync(session.Id, profile.Id, cancellationToken);
        return RsvpMapper.ToModel(result, pickupPalSync);
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
    IRsvpRepository rsvpRepository,
    IRsvpPickupPalSyncService pickupPalSyncService)
{
    public async Task<RsvpResultModel> HandleAsync(CancelRsvpCommand command, CancellationToken cancellationToken = default)
    {
        var profile = await SubmitRsvpCommandHandler.GetCurrentProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var session = await SubmitRsvpCommandHandler.GetOpenSessionAsync(sessionRepository, command.SessionId, clock.UtcNow, cancellationToken);

        // The whole waitlist is evaluated in one batched compliance read instead of two queries per
        // candidate, but still inside the repository's transaction so an expiry never acts on a
        // verdict that was read before the transaction opened.
        var result = await rsvpRepository.CancelAndPromoteAsync(
            session.Id,
            profile.Id,
            (candidatePlayerProfileIds, token) =>
                eligibilityService.CheckManyAsync(candidatePlayerProfileIds, session.Id, token),
            cancellationToken);

        // Runs after the local transaction committed and never fails the cancel (RSVP-9). The
        // player promoted from the local waitlist is now Going and is pushed too, best effort.
        var pickupPalSync = await pickupPalSyncService.SyncAfterLocalWriteAsync(session.Id, profile.Id, cancellationToken);
        if (result.PromotedPlayerProfileId is { } promotedPlayerProfileId)
        {
            await pickupPalSyncService.SyncAfterLocalWriteAsync(session.Id, promotedPlayerProfileId, cancellationToken);
        }

        return RsvpMapper.ToModel(result, pickupPalSync);
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
    IRsvpRepository rsvpRepository,
    IRsvpPickupPalSyncService pickupPalSyncService)
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

        // Runs after the local transaction committed and never fails the override (RSVP-9).
        var pickupPalSync = await pickupPalSyncService.SyncAfterLocalWriteAsync(command.SessionId, command.PlayerProfileId, cancellationToken);
        return RsvpMapper.ToModel(result, pickupPalSync);
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
        if (session.Status != SessionStatus.Published)
        {
            throw new ApplicationConflictException("Check-in is not available for this session.");
        }
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

        var effectiveOutcome = isOutsideCheckInWindow ? AttendanceOutcome.Late : command.Outcome;
        var result = await rsvpRepository.RecordCheckInAsync(
            command.SessionId,
            command.PlayerProfileId,
            adminProfile.Id,
            nowUtc,
            effectiveOutcome,
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

public sealed class SelfCheckInCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IPlayerSessionEligibilityService eligibilityService,
    IRsvpRepository rsvpRepository,
    IGroupMembershipGate groupMembershipGate)
{
    public async Task<CheckInResultModel> HandleAsync(
        SelfCheckInCommand command,
        CancellationToken cancellationToken = default)
    {
        var profile = await SubmitRsvpCommandHandler.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");
        if (session.Status != SessionStatus.Published)
        {
            throw new ApplicationConflictException("Check-in is not available for this session.");
        }

        // GRP-1: self check-in is a group-scoped action (admin check-in is not gated).
        await groupMembershipGate.EnsureCanJoinAsync(session, profile.Id, cancellationToken);

        var nowUtc = clock.UtcNow;
        if (nowUtc < session.CheckInOpensAtUtc || nowUtc > session.CheckInClosesAtUtc)
        {
            throw new ApplicationConflictException("Self check-in is outside the session check-in window.");
        }

        var attendance = await rsvpRepository.GetGameDayAttendanceAsync(
            session.Id,
            profile.Id,
            cancellationToken);
        // Going and waitlisted players may both self check in (a waitlisted player who arrives often
        // takes a no-show's place); only someone with no confirmed spot at all is turned away.
        if (!attendance.IsCurrentPlayerGoing && !attendance.IsCurrentPlayerWaitlisted)
        {
            throw new ApplicationConflictException("A Going or waitlist spot is required to check in.");
        }

        var eligibility = await eligibilityService.CheckAsync(profile.Id, session.Id, cancellationToken);
        if (!eligibility.IsEligible)
        {
            throw new ApplicationConflictException(eligibility.Reason ?? "Player is not eligible to check in.");
        }

        var result = await rsvpRepository.RecordCheckInAsync(
            session.Id,
            profile.Id,
            profile.Id,
            nowUtc,
            AttendanceOutcome.CheckedIn,
            cancellationToken: cancellationToken);
        var checkIn = result.CheckIn;

        return new CheckInResultModel(
            checkIn.Id,
            checkIn.SessionId,
            checkIn.PlayerProfileId,
            checkIn.CheckedInByPlayerProfileId!.Value,
            checkIn.CheckedInAtUtc,
            checkIn.Outcome.ToString(),
            false,
            null,
            null);
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
