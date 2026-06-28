using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class UpdateMyProfileCommandHandler(
    ICurrentUser currentUser,
    IValidator<UpdateMyProfileCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<PlayerProfileModel> HandleAsync(
        UpdateMyProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        profile.DisplayName = command.DisplayName.Trim();
        profile.NormalizedDisplayName = profile.DisplayName.ToUpperInvariant();
        profile.PreferredPosition = command.PreferredPosition.Trim();
        profile.PhotoUri = string.IsNullOrWhiteSpace(command.PhotoUri) ? null : command.PhotoUri.Trim();
        playerProfileRepository.Update(profile);

        await UpsertEmergencyContactAsync(profile.Id, command.EmergencyContact, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var emergencyContact = await playerProfileRepository.FindEmergencyContactAsync(profile.Id, cancellationToken);
        return PlayerProfileMapper.ToModel(profile, emergencyContact);
    }

    private async Task UpsertEmergencyContactAsync(
        Guid playerProfileId,
        EmergencyContactModel? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return;
        }

        var existing = await playerProfileRepository.FindEmergencyContactAsync(playerProfileId, cancellationToken);
        if (existing is null)
        {
            await playerProfileRepository.AddEmergencyContactAsync(
                new EmergencyContact
                {
                    Id = Guid.NewGuid(),
                    PlayerProfileId = playerProfileId,
                    Name = command.Name.Trim(),
                    PhoneNumberHash = PhonePrivacy.Hash(command.PhoneNumber),
                    MaskedPhoneNumber = PhonePrivacy.Mask(command.PhoneNumber),
                    Relationship = string.IsNullOrWhiteSpace(command.Relationship) ? null : command.Relationship.Trim(),
                },
                cancellationToken);
            return;
        }

        existing.Name = command.Name.Trim();
        existing.PhoneNumberHash = PhonePrivacy.Hash(command.PhoneNumber);
        existing.MaskedPhoneNumber = PhonePrivacy.Mask(command.PhoneNumber);
        existing.Relationship = string.IsNullOrWhiteSpace(command.Relationship) ? null : command.Relationship.Trim();
        playerProfileRepository.UpdateEmergencyContact(existing);
    }
}
