using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class CreateProfileMergeCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<CreateProfileMergeCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ProfileMergeResult> HandleAsync(
        CreateProfileMergeCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var source = await playerProfileRepository.FindProfileAsync(command.SourceGuestPlayerProfileId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Source guest profile was not found.");
        var target = await playerProfileRepository.FindProfileAsync(command.TargetPlayerProfileId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Target player profile was not found.");

        if (!source.IsGuest || source.IdentityUserId is not null)
        {
            throw new ApplicationConflictException("Source profile must be an unclaimed guest profile.");
        }

        if (target.IsGuest || target.IdentityUserId is null)
        {
            throw new ApplicationConflictException("Target profile must be a claimed player profile.");
        }

        var now = clock.UtcNow;
        var merge = new ProfileMerge
        {
            Id = Guid.NewGuid(),
            SourcePlayerProfileId = source.Id,
            TargetPlayerProfileId = target.Id,
            Status = ProfileMergeStatus.Completed,
            MergedAtUtc = now,
            MergedByActorType = currentUser.UserId is null ? AuditActorType.System : AuditActorType.User,
            MergedByActorId = currentUser.UserId?.ToString("D"),
        };

        source.IsDeleted = true;
        playerProfileRepository.Update(source);
        await playerProfileRepository.AddProfileMergeAsync(merge, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProfileMergeResult(
            merge.Id,
            source.Id,
            target.Id,
            merge.Status.ToString());
    }
}
