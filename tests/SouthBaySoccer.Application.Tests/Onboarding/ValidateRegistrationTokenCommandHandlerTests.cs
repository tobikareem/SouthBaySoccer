using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;

namespace SouthBaySoccer.Application.Tests.Onboarding;

public sealed class ValidateRegistrationTokenCommandHandlerTests
{
    private const string Token = "8f14e45f-ceea-467a-9a1c-2b3d4e5f6a7b";

    private readonly Mock<IPickupPalOnboardingClient> onboardingClient = new();
    private readonly Mock<IPickupPalUserClient> userClient = new();

    [Fact]
    public async Task HandleAsync_WhenTokenValidAndPhoneUnregistered_ReturnsMaskedPhone()
    {
        SetupValidation(new RegistrationTokenValidation(RegistrationTokenStatus.Valid, "15550001234"));
        userClient
            .Setup(x => x.FindByPhoneAsync("15550001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickupPalUser?)null);

        var result = await CreateHandler().HandleAsync(new ValidateRegistrationTokenCommand(Token));

        result.PhoneMasked.Should().Be("+******1234");
    }

    [Theory]
    [InlineData(RegistrationTokenStatus.Expired, OnboardingTokenFailure.Expired)]
    [InlineData(RegistrationTokenStatus.Invalid, OnboardingTokenFailure.Invalid)]
    public async Task HandleAsync_WhenTokenNotValid_ThrowsMatchingFailure(
        RegistrationTokenStatus status,
        OnboardingTokenFailure expected)
    {
        SetupValidation(new RegistrationTokenValidation(status, null));

        var act = () => CreateHandler().HandleAsync(new ValidateRegistrationTokenCommand(Token));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(expected);
        userClient.Verify(x => x.FindByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenPhoneAlreadyHasAccount_ThrowsAlreadyRegistered()
    {
        SetupValidation(new RegistrationTokenValidation(RegistrationTokenStatus.Valid, "15550001234"));
        userClient
            .Setup(x => x.FindByPhoneAsync("15550001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalUser("existing", null, "15550001234", null, null, null, null, Array.Empty<string>(), null));

        var act = () => CreateHandler().HandleAsync(new ValidateRegistrationTokenCommand(Token));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.AlreadyRegistered);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenValidWithoutPhone_ThrowsInvalidLikeTheRegisterStep()
    {
        SetupValidation(new RegistrationTokenValidation(RegistrationTokenStatus.Valid, null));

        var act = () => CreateHandler().HandleAsync(new ValidateRegistrationTokenCommand(Token));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Invalid);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenBlank_ThrowsValidationException()
    {
        var act = () => CreateHandler().HandleAsync(new ValidateRegistrationTokenCommand(" "));

        await act.Should().ThrowAsync<ValidationException>();
        onboardingClient.Verify(
            x => x.ValidateRegistrationTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupValidation(RegistrationTokenValidation validation) =>
        onboardingClient
            .Setup(x => x.ValidateRegistrationTokenAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validation);

    private ValidateRegistrationTokenCommandHandler CreateHandler() =>
        new(new ValidateRegistrationTokenCommandValidator(), onboardingClient.Object, userClient.Object);
}
