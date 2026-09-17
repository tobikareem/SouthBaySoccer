using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Onboarding;

public sealed class RegisterWithWhatsAppCommandHandlerTests
{
    private const string Token = "8f14e45f-ceea-467a-9a1c-2b3d4e5f6a7b";
    private const string TermsVersion = "20250708";
    private static readonly DateTime Now = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);

    private static readonly RegisterWithWhatsAppCommand Command = new(
        Token,
        " Andre ",
        "Silva",
        "andre.silva@example.com",
        "pickup-ball-2026",
        "st",
        TermsVersion,
        Now.AddMinutes(-1));

    private static readonly PickupPalUser CreatedUser = new(
        "clx8f9a2b0001qwer5678efgh",
        "andre.silva@example.com",
        "15550001234",
        "Andre",
        "Silva",
        null,
        null,
        Array.Empty<string>(),
        Now);

    private readonly List<string> calls = [];
    private readonly Mock<IPickupPalOnboardingClient> onboardingClient = new();
    private readonly Mock<IPlayerRegistrationRepository> registrations = new();
    private readonly Mock<IOutboxMessageRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IPickupPalUserSyncService> syncService = new();
    private readonly Mock<IPickupPalGroupClient> groupClient = new();
    private readonly Mock<IAuthenticationTokenIssuer> tokenIssuer = new(MockBehavior.Strict);
    private readonly AuthenticationTokenSubject subject = new(Guid.NewGuid(), Guid.NewGuid(), new[] { "Player" });
    private readonly AuthenticationTokenSet tokens = new("access", "refresh", Now.AddMinutes(15));
    private readonly List<PlayerRegistration> added = [];
    private readonly List<OutboxMessage> enqueued = [];

    public RegisterWithWhatsAppCommandHandlerTests()
    {
        onboardingClient
            .Setup(x => x.ValidateRegistrationTokenAsync(Token, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("validate"))
            .ReturnsAsync(new RegistrationTokenValidation(RegistrationTokenStatus.Valid, "15550001234"));
        onboardingClient
            .Setup(x => x.IsEmailAvailableAsync("andre.silva@example.com", It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("email"))
            .ReturnsAsync(true);
        onboardingClient
            .Setup(x => x.RegisterWithTokenAsync(It.IsAny<PickupPalRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("register"))
            .ReturnsAsync(CreatedUser);
        registrations
            .Setup(x => x.FindAwaitingExternalByPhoneNumberHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerRegistration?)null);
        registrations
            .Setup(x => x.AddAsync(It.IsAny<PlayerRegistration>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerRegistration, CancellationToken>((registration, _) =>
            {
                calls.Add("persist-local");
                added.Add(registration);
            })
            .Returns(Task.CompletedTask);
        outbox
            .Setup(x => x.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, CancellationToken>((message, _) => enqueued.Add(message))
            .Returns(Task.CompletedTask);
        syncService
            .Setup(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("sync"))
            .ReturnsAsync(subject);
        groupClient
            .Setup(x => x.GetLinkedGroupsAsync(CreatedUser.Id, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("groups"))
            .ReturnsAsync([new PickupPalGroupChat("g1", "South Bay Sunday", null, "ACTIVE", 40, null)]);
        tokenIssuer
            .Setup(x => x.IssueTokensAsync(subject, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("tokens"))
            .ReturnsAsync(tokens);
    }

    [Fact]
    public async Task HandleAsync_WhenRegistrationSucceeds_PersistsLocallyBeforePickupPalThenSyncsRereadsGroupsAndIssuesTokens()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(Command);

        calls.Should().Equal("validate", "email", "persist-local", "register", "sync", "groups", "tokens");
        result.Tokens.Should().Be(tokens);
        result.FirstName.Should().Be("Andre");
        result.PhoneMasked.Should().Be("+******1234");
        result.GroupNames.Should().Equal("South Bay Sunday");
        result.HistorySyncPending.Should().BeFalse();

        var registration = added.Should().ContainSingle().Subject;
        registration.Status.Should().Be(PlayerRegistrationStatus.Completed);
        registration.PickupPalUserId.Should().Be(CreatedUser.Id);
        registration.CompletedAtUtc.Should().Be(Now);
        registration.ExternalAttemptCount.Should().Be(1);
        registration.Source.Should().Be(RegistrationSource.WhatsAppToken);
        registration.PhoneNumberHash.Should().HaveLength(64).And.NotContain("1234");
        registration.Email.Should().Be("andre.silva@example.com");
        registration.PreferredPosition.Should().Be("st");
        registration.LastExternalError.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenRegistrationSucceeds_ForwardsPasswordOnceAndNeverStoresIt()
    {
        PickupPalRegistrationRequest? forwarded = null;
        onboardingClient
            .Setup(x => x.RegisterWithTokenAsync(It.IsAny<PickupPalRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PickupPalRegistrationRequest, CancellationToken>((request, _) => forwarded = request)
            .ReturnsAsync(CreatedUser);
        var handler = CreateHandler();

        await handler.HandleAsync(Command);

        forwarded.Should().NotBeNull();
        forwarded!.Password.Should().Be("pickup-ball-2026");
        forwarded.Token.Should().Be(Token);
        forwarded.TermsVersion.Should().Be(TermsVersion);
        typeof(PlayerRegistration).GetProperties().Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_WhenRegistrationSucceeds_SyncsPreferredPositionFromTheForm()
    {
        PickupPalUser? synced = null;
        syncService
            .Setup(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()))
            .Callback<PickupPalUser, CancellationToken>((user, _) => synced = user)
            .ReturnsAsync(subject);
        var handler = CreateHandler();

        await handler.HandleAsync(Command);

        synced.Should().NotBeNull();
        synced!.Id.Should().Be(CreatedUser.Id);
        synced.PreferredPositions.Should().Equal("st");
    }

    [Fact]
    public async Task HandleAsync_WhenSyncThrowsAfterExternalCreate_KeepsPickupPalUserIdAndExternalCreatedStatus()
    {
        // The Pickup Pal account exists at this point; the local row must already say so before
        // sync runs, otherwise a sync failure would orphan an upstream account we cannot find again.
        var statusesAtSync = new List<(PlayerRegistrationStatus Status, string? PickupPalUserId, int Saves)>();
        var saves = 0;
        unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saves++)
            .ReturnsAsync(1);
        syncService
            .Setup(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()))
            .Callback(() => statusesAtSync.Add((added[0].Status, added[0].PickupPalUserId, saves)))
            .ThrowsAsync(new InvalidOperationException("identity store failed"));
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command);

        await act.Should().ThrowAsync<InvalidOperationException>();
        statusesAtSync.Should().ContainSingle().Which.Should().Be((PlayerRegistrationStatus.ExternalCreated, CreatedUser.Id, 2));
        var registration = added.Should().ContainSingle().Subject;
        registration.Status.Should().Be(PlayerRegistrationStatus.ExternalCreated);
        registration.PickupPalUserId.Should().Be(CreatedUser.Id);
        registration.CompletedAtUtc.Should().BeNull();
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenPickupPalUnavailable_LeavesRegistrationExternalFailedEnqueuesOutboxAndIssuesNoTokens()
    {
        onboardingClient
            .Setup(x => x.RegisterWithTokenAsync(It.IsAny<PickupPalRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("Pickup Pal is unavailable right now. Try again later."));
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command);

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
        var registration = added.Should().ContainSingle().Subject;
        registration.Status.Should().Be(PlayerRegistrationStatus.ExternalFailed);
        registration.LastExternalError.Should().Be("PickupPalUnavailable");
        registration.PickupPalUserId.Should().BeNull();
        registrations.Verify(x => x.SoftDelete(It.IsAny<PlayerRegistration>()), Times.Never);
        var message = enqueued.Should().ContainSingle().Subject;
        message.MessageType.Should().Be(OnboardingOutboxMessages.PlayerRegistrationExternalFailed);
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.PayloadJson.Should().Contain(registration.Id.ToString()).And.NotContain("1234").And.NotContain("Andre");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        syncService.Verify(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenPickupPalRejectsToken_MarksExternalFailedWithoutOutboxAndThrowsInvalid()
    {
        onboardingClient
            .Setup(x => x.RegisterWithTokenAsync(It.IsAny<PickupPalRegistrationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PickupPalOnboardingException(PickupPalOnboardingFailure.TokenInvalid, "Invalid registration token"));
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command);

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Invalid);
        added.Should().ContainSingle().Which.Status.Should().Be(PlayerRegistrationStatus.ExternalFailed);
        added[0].LastExternalError.Should().Be("TokenInvalid");
        enqueued.Should().BeEmpty();
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenRetryingAfterFailure_ReusesTheAwaitingRegistrationRow()
    {
        var existing = new PlayerRegistration
        {
            Id = Guid.NewGuid(),
            PhoneNumberHash = "existing",
            Status = PlayerRegistrationStatus.ExternalFailed,
            ExternalAttemptCount = 1,
            LastExternalError = "PickupPalUnavailable",
        };
        registrations
            .Setup(x => x.FindAwaitingExternalByPhoneNumberHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var handler = CreateHandler();

        await handler.HandleAsync(Command);

        added.Should().BeEmpty();
        existing.ExternalAttemptCount.Should().Be(2);
        existing.Status.Should().Be(PlayerRegistrationStatus.Completed);
        existing.FirstName.Should().Be("Andre");
        registrations.Verify(x => x.Update(existing), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(RegistrationTokenStatus.Expired, OnboardingTokenFailure.Expired)]
    [InlineData(RegistrationTokenStatus.Invalid, OnboardingTokenFailure.Invalid)]
    public async Task HandleAsync_WhenTokenNotValid_ThrowsWithoutPersistingOrRegistering(
        RegistrationTokenStatus status,
        OnboardingTokenFailure expected)
    {
        onboardingClient
            .Setup(x => x.ValidateRegistrationTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationTokenValidation(status, null));
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command);

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(expected);
        added.Should().BeEmpty();
        onboardingClient.Verify(
            x => x.RegisterWithTokenAsync(It.IsAny<PickupPalRegistrationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenEmailTaken_ThrowsEmailAlreadyRegisteredWithoutSpendingToken()
    {
        onboardingClient
            .Setup(x => x.IsEmailAvailableAsync("andre.silva@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command);

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.EmailAlreadyRegistered);
        added.Should().BeEmpty();
        onboardingClient.Verify(
            x => x.RegisterWithTokenAsync(It.IsAny<PickupPalRegistrationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenGroupRereadFails_CompletesRegistrationAndReportsHistorySyncPending()
    {
        groupClient
            .Setup(x => x.GetLinkedGroupsAsync(CreatedUser.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(Command);

        result.Tokens.Should().Be(tokens);
        result.GroupNames.Should().BeEmpty();
        result.HistorySyncPending.Should().BeTrue();
        added.Should().ContainSingle().Which.Status.Should().Be(PlayerRegistrationStatus.Completed);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    public async Task HandleAsync_WhenPasswordTooShort_ThrowsValidationException(string password)
    {
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command with { Password = password });

        await act.Should().ThrowAsync<ValidationException>();
        onboardingClient.Verify(
            x => x.ValidateRegistrationTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTermsVersionIsNotCurrent_ThrowsValidationException()
    {
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command with { TermsVersion = "20240101" });

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(error => error.PropertyName == nameof(RegisterWithWhatsAppCommand.TermsVersion));
    }

    [Fact]
    public async Task HandleAsync_WhenEmailMalformed_ThrowsValidationException()
    {
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(Command with { Email = "not-an-email" });

        await act.Should().ThrowAsync<ValidationException>();
    }

    private RegisterWithWhatsAppCommandHandler CreateHandler()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        var policy = new Mock<IOnboardingPolicy>();
        policy.SetupGet(x => x.TermsVersion).Returns(TermsVersion);

        return new RegisterWithWhatsAppCommandHandler(
            new RegisterWithWhatsAppCommandValidator(policy.Object, clock.Object),
            onboardingClient.Object,
            registrations.Object,
            outbox.Object,
            unitOfWork.Object,
            clock.Object,
            syncService.Object,
            groupClient.Object,
            tokenIssuer.Object);
    }
}
