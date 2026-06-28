using FluentValidation;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class CreateGuestProfileCommandHandler(
    IValidator<CreateGuestProfileCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<PlayerProfileModel> HandleAsync(
        CreateGuestProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var displayName = command.DisplayName.Trim();
        var profile = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            NormalizedDisplayName = displayName.ToUpperInvariant(),
            PreferredPosition = command.PreferredPosition.Trim(),
            PhotoUri = string.IsNullOrWhiteSpace(command.PhotoUri) ? null : command.PhotoUri.Trim(),
            IsGuest = true,
            Role = PlayerRole.Guest,
        };

        await playerProfileRepository.AddAsync(profile, cancellationToken);
        EmergencyContact? emergencyContact = null;
        if (command.EmergencyContact is not null)
        {
            emergencyContact = new EmergencyContact
            {
                Id = Guid.NewGuid(),
                PlayerProfileId = profile.Id,
                Name = command.EmergencyContact.Name.Trim(),
                PhoneNumberHash = PhonePrivacy.Hash(command.EmergencyContact.PhoneNumber),
                MaskedPhoneNumber = PhonePrivacy.Mask(command.EmergencyContact.PhoneNumber),
                Relationship = string.IsNullOrWhiteSpace(command.EmergencyContact.Relationship)
                    ? null
                    : command.EmergencyContact.Relationship.Trim(),
            };
            await playerProfileRepository.AddEmergencyContactAsync(emergencyContact, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PlayerProfileMapper.ToModel(profile, emergencyContact);
    }
}
