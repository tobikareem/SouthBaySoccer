using System.Text.Json;
using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Creates a Pickup Pal account with a WhatsApp-bound registration token, then syncs the local
/// identity and issues session tokens.
/// </summary>
/// <param name="Token">The single-use registration token.</param>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name.</param>
/// <param name="Email">The email; must be available on Pickup Pal.</param>
/// <param name="Password">The password, forwarded once to Pickup Pal and never stored.</param>
/// <param name="PreferredPosition">The optional preferred position.</param>
/// <param name="TermsVersion">The terms version accepted; must match the current version.</param>
/// <param name="TermsAcceptedAtUtc">When the terms were accepted (UTC).</param>
public sealed record RegisterWithWhatsAppCommand(
    string Token,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? PreferredPosition,
    string TermsVersion,
    DateTime TermsAcceptedAtUtc);

/// <summary>What the welcome screen shows once the linked account exists.</summary>
/// <param name="Tokens">The issued session tokens.</param>
/// <param name="FirstName">The registered first name.</param>
/// <param name="PhoneMasked">The masked verified phone.</param>
/// <param name="GroupNames">Group memberships re-read from Pickup Pal after creation.</param>
/// <param name="HistorySyncPending">Whether the group re-read failed, so membership is not yet known.</param>
public sealed record RegistrationCompleted(
    AuthenticationTokenSet Tokens,
    string FirstName,
    string PhoneMasked,
    IReadOnlyList<string> GroupNames,
    bool HistorySyncPending);

/// <summary>Field limits shared by the registration validators.</summary>
internal static class RegistrationRules
{
    public const int NameMaxLength = 80;
    public const int EmailMaxLength = 256;
    public const int PasswordMinLength = 6;
    public const int PasswordMaxLength = 128;
    public const int PreferredPositionMaxLength = 64;
    public const int TermsVersionMaxLength = 32;
}

/// <summary>Validates <see cref="RegisterWithWhatsAppCommand"/>.</summary>
public sealed class RegisterWithWhatsAppCommandValidator : AbstractValidator<RegisterWithWhatsAppCommand>
{
    /// <summary>Terms acceptance may not sit further ahead of the server clock than this.</summary>
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Initializes a new instance of the <see cref="RegisterWithWhatsAppCommandValidator"/> class.</summary>
    public RegisterWithWhatsAppCommandValidator(IOnboardingPolicy policy, IClock clock)
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(OnboardingTokenRules.MaxLength);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(RegistrationRules.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(RegistrationRules.NameMaxLength);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(RegistrationRules.EmailMaxLength).EmailAddress();
        // Pickup Pal does not enforce password length on sign-up, and omitting one creates an account
        // that can never log in, so the minimum is enforced here.
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(RegistrationRules.PasswordMinLength)
            .MaximumLength(RegistrationRules.PasswordMaxLength);
        RuleFor(x => x.PreferredPosition).MaximumLength(RegistrationRules.PreferredPositionMaxLength);
        RuleFor(x => x.TermsVersion)
            .NotEmpty()
            .MaximumLength(RegistrationRules.TermsVersionMaxLength)
            .Equal(policy.TermsVersion)
            .WithMessage("The accepted terms version is not current.");
        RuleFor(x => x.TermsAcceptedAtUtc)
            .Must(value => value != default && value <= clock.UtcNow.Add(AllowedClockSkew))
            .WithMessage("Terms acceptance time is invalid.");
    }
}

/// <summary>
/// Handles <see cref="RegisterWithWhatsAppCommand"/>: validate token, check email, persist the
/// registration locally, create the Pickup Pal account, sync local identity, re-read groups, issue
/// tokens. The local row is written before Pickup Pal is called and is never deleted on failure.
/// </summary>
public sealed class RegisterWithWhatsAppCommandHandler(
    IValidator<RegisterWithWhatsAppCommand> validator,
    IPickupPalOnboardingClient onboardingClient,
    IPlayerRegistrationRepository registrationRepository,
    IOutboxMessageRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPickupPalUserSyncService pickupPalUserSyncService,
    IPickupPalGroupClient groupClient,
    IAuthenticationTokenIssuer tokenIssuer,
    GroupNameVisibility groupNameVisibility)
{
    /// <summary>Runs the registration pipeline.</summary>
    /// <exception cref="OnboardingTokenException">The token or email cannot be used; nothing external was created.</exception>
    /// <exception cref="ApplicationServiceUnavailableException">Pickup Pal could not be reached; the local registration is kept.</exception>
    public async Task<RegistrationCompleted> HandleAsync(
        RegisterWithWhatsAppCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var token = command.Token.Trim();
        var email = command.Email.Trim();

        var validation = await onboardingClient.ValidateRegistrationTokenAsync(token, cancellationToken);
        var phoneNumberDigits = validation.Status switch
        {
            RegistrationTokenStatus.Expired => throw new OnboardingTokenException(OnboardingTokenFailure.Expired),
            RegistrationTokenStatus.Invalid => throw new OnboardingTokenException(OnboardingTokenFailure.Invalid),
            _ => validation.PhoneNumberDigits
                ?? throw new OnboardingTokenException(OnboardingTokenFailure.Invalid),
        };

        if (!await onboardingClient.IsEmailAvailableAsync(email, cancellationToken))
        {
            throw new OnboardingTokenException(OnboardingTokenFailure.EmailAlreadyRegistered);
        }

        // Local first: the registration exists in our database before Pickup Pal hears about it.
        var registration = await UpsertPendingRegistrationAsync(command, email, phoneNumberDigits, cancellationToken);

        PickupPalUser createdUser;
        try
        {
            createdUser = await onboardingClient.RegisterWithTokenAsync(
                new PickupPalRegistrationRequest(
                    token,
                    registration.FirstName,
                    registration.LastName,
                    email,
                    command.Password,
                    registration.TermsVersion,
                    registration.TermsAcceptedAtUtc),
                cancellationToken);
        }
        catch (PickupPalOnboardingException exception)
        {
            await MarkExternalFailedAsync(registration, exception.Failure.ToString(), enqueueOutbox: false, cancellationToken);
            throw ToTokenException(exception);
        }
        catch (ApplicationServiceUnavailableException)
        {
            await MarkExternalFailedAsync(registration, "PickupPalUnavailable", enqueueOutbox: true, cancellationToken);
            throw;
        }

        // The Pickup Pal account now exists: record its id before anything else can fail, so a
        // sync failure never leaves an external account our database does not know about.
        registration.Status = PlayerRegistrationStatus.ExternalCreated;
        registration.PickupPalUserId = createdUser.Id;
        registration.LastExternalError = null;
        registrationRepository.Update(registration);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var user = createdUser with
        {
            PreferredPositions = string.IsNullOrWhiteSpace(registration.PreferredPosition)
                ? createdUser.PreferredPositions
                : [registration.PreferredPosition],
        };

        var subject = await pickupPalUserSyncService.SyncAsync(user, cancellationToken);

        registration.Status = PlayerRegistrationStatus.Completed;
        registration.CompletedAtUtc = clock.UtcNow;
        registrationRepository.Update(registration);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (groupNames, historySyncPending) = await ReadGroupsAsync(user.Id, cancellationToken);
        var tokens = await tokenIssuer.IssueTokensAsync(subject, cancellationToken);

        return new RegistrationCompleted(
            tokens,
            registration.FirstName,
            registration.PhoneMasked,
            groupNames,
            historySyncPending);
    }

    private async Task<PlayerRegistration> UpsertPendingRegistrationAsync(
        RegisterWithWhatsAppCommand command,
        string email,
        string phoneNumberDigits,
        CancellationToken cancellationToken)
    {
        var phoneNumberHash = OnboardingPhone.Hash(phoneNumberDigits);
        var registration = await registrationRepository.FindAwaitingExternalByPhoneNumberHashAsync(
            phoneNumberHash,
            cancellationToken);
        var isNew = registration is null;
        registration ??= new PlayerRegistration
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = phoneNumberHash,
            Source = RegistrationSource.WhatsAppToken,
        };

        registration.FirstName = command.FirstName.Trim();
        registration.LastName = command.LastName.Trim();
        registration.Email = email;
        registration.PhoneMasked = OnboardingPhone.Mask(phoneNumberDigits);
        registration.PreferredPosition = string.IsNullOrWhiteSpace(command.PreferredPosition)
            ? null
            : command.PreferredPosition.Trim();
        registration.TermsVersion = command.TermsVersion.Trim();
        registration.TermsAcceptedAtUtc = command.TermsAcceptedAtUtc;
        registration.Status = PlayerRegistrationStatus.PendingExternal;
        registration.ExternalAttemptCount += 1;
        registration.LastExternalError = null;

        if (isNew)
        {
            await registrationRepository.AddAsync(registration, cancellationToken);
        }
        else
        {
            registrationRepository.Update(registration);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return registration;
    }

    private async Task MarkExternalFailedAsync(
        PlayerRegistration registration,
        string failureCode,
        bool enqueueOutbox,
        CancellationToken cancellationToken)
    {
        registration.Status = PlayerRegistrationStatus.ExternalFailed;
        registration.LastExternalError = failureCode;
        registrationRepository.Update(registration);

        if (enqueueOutbox)
        {
            var now = clock.UtcNow;
            await outboxRepository.AddAsync(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    MessageType = OnboardingOutboxMessages.PlayerRegistrationExternalFailed,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        registration.ExternalAttemptCount,
                        FailureCode = failureCode,
                        FailedAtUtc = now,
                    }),
                    Status = OutboxMessageStatus.Pending,
                    AvailableAtUtc = now,
                },
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<(IReadOnlyList<string> GroupNames, bool HistorySyncPending)> ReadGroupsAsync(
        string pickupPalUserId,
        CancellationToken cancellationToken)
    {
        // A 201 from Pickup Pal does not mean the auto-join ran (it is best-effort and swallowed on
        // their side), so membership is only ever shown after this re-read. A failed re-read is not
        // a failed registration; the welcome screen shows "still syncing" instead.
        try
        {
            var groups = await groupClient.GetLinkedGroupsAsync(pickupPalUserId, cancellationToken);
            return (groups.Select(group => group.GroupName)
                .Where(name => name.Length > 0 && groupNameVisibility.IsVisible(name)).ToArray(), false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (Array.Empty<string>(), true);
        }
    }

    private static OnboardingTokenException ToTokenException(PickupPalOnboardingException exception) =>
        exception.Failure switch
        {
            PickupPalOnboardingFailure.TokenExpired => new OnboardingTokenException(OnboardingTokenFailure.Expired),
            PickupPalOnboardingFailure.TokenInvalid => new OnboardingTokenException(OnboardingTokenFailure.Invalid),
            PickupPalOnboardingFailure.EmailAlreadyRegistered => new OnboardingTokenException(OnboardingTokenFailure.EmailAlreadyRegistered),
            PickupPalOnboardingFailure.PhoneAlreadyRegistered => new OnboardingTokenException(OnboardingTokenFailure.AlreadyRegistered),
            // Pickup Pal's remaining 400s are payload validation we already enforce; the token has
            // been burned by then, so the honest outcome for the player is "send the message again".
            _ => new OnboardingTokenException(OnboardingTokenFailure.Invalid),
        };
}
