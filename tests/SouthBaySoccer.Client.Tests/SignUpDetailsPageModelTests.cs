using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class SignUpDetailsPageModelTests
{
    private static readonly RegistrationCompletedResponse Completed = new(
        new AuthenticationTokensResponse("a", "r", new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        "Ada",
        "+1 (555) ••• 9421",
        ["Saturday crew"],
        true);

    [Theory]
    [InlineData("", "Okafor", "ada@example.com", "secret1", true)]
    [InlineData("Ada", "Okafor", "not-an-email", "secret1", true)]
    [InlineData("Ada", "Okafor", "ada@example.com", "short", true)]
    [InlineData("Ada", "Okafor", "ada@example.com", "secret1", false)]
    public async Task CreateAccount_InvalidForm_DoesNotCallServiceOrSpendToken(
        string first, string last, string email, string password, bool terms)
    {
        var client = new Mock<IOnboardingClient>(MockBehavior.Strict);
        var navigator = new Mock<IOnboardingNavigator>(MockBehavior.Strict);
        var pageModel = Create(client, navigator);
        pageModel.FirstName = first;
        pageModel.LastName = last;
        pageModel.Email = email;
        pageModel.Password = password;
        pageModel.TermsAccepted = terms;

        await pageModel.CreateAccountCommand.ExecuteAsync(null);

        (pageModel.HasFormError || pageModel.HasEmailError).Should().BeTrue();
        client.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAccount_EmailTaken_ShowsInlineErrorWithoutRegistering()
    {
        var client = new Mock<IOnboardingClient>(MockBehavior.Strict);
        client.Setup(c => c.IsEmailAvailableAsync("taken@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var navigator = new Mock<IOnboardingNavigator>(MockBehavior.Strict);
        var pageModel = CreateValid(client, navigator, "taken@example.com");

        await pageModel.CreateAccountCommand.ExecuteAsync(null);

        pageModel.EmailError.Should().Be(SignUpDetailsPageModel.EmailTakenMessage);
        client.Verify(c => c.RegisterAsync(It.IsAny<RegisterWithWhatsAppRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        pageModel.Email = "ada2@example.com";
        pageModel.HasEmailError.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAccount_Valid_RegistersWithTokenAndCurrentTermsThenShowsWelcome()
    {
        var client = new Mock<IOnboardingClient>(MockBehavior.Strict);
        client.Setup(c => c.IsEmailAvailableAsync("ada@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        client.Setup(c => c.GetTermsVersionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("20250708");
        RegisterWithWhatsAppRequest? sent = null;
        client.Setup(c => c.RegisterAsync(It.IsAny<RegisterWithWhatsAppRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RegisterWithWhatsAppRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(Completed);
        var navigator = new Mock<IOnboardingNavigator>(MockBehavior.Strict);
        navigator.Setup(n => n.ShowSignUpWelcomeAsync(Completed, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var pageModel = CreateValid(client, navigator, "ada@example.com");
        pageModel.SelectedPosition = pageModel.Positions[3];

        await pageModel.CreateAccountCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.Token.Should().Be("tok");
        sent.TermsVersion.Should().Be("20250708");
        sent.TermsAcceptedAtUtc.Should().Be(new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc));
        sent.PreferredPosition.Should().Be("CM");
        sent.FirstName.Should().Be("Ada");
        navigator.VerifyAll();
        pageModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAccount_TokenSpent_ShowsExpiredScreen()
    {
        var client = new Mock<IOnboardingClient>(MockBehavior.Strict);
        client.Setup(c => c.IsEmailAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        client.Setup(c => c.GetTermsVersionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("20250708");
        client.Setup(c => c.RegisterAsync(It.IsAny<RegisterWithWhatsAppRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OnboardingTokenException(OnboardingTokenFailure.Invalid));
        var navigator = new Mock<IOnboardingNavigator>(MockBehavior.Strict);
        navigator.Setup(n => n.ShowSignUpExpiredAsync(OnboardingTokenFailure.Invalid, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var pageModel = CreateValid(client, navigator, "ada@example.com");

        await pageModel.CreateAccountCommand.ExecuteAsync(null);

        navigator.VerifyAll();
    }

    [Fact]
    public async Task CreateAccount_ServiceDown_IsRecoverable()
    {
        var client = new Mock<IOnboardingClient>(MockBehavior.Strict);
        client.Setup(c => c.IsEmailAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var navigator = new Mock<IOnboardingNavigator>(MockBehavior.Strict);
        var pageModel = CreateValid(client, navigator, "ada@example.com");

        await pageModel.CreateAccountCommand.ExecuteAsync(null);

        pageModel.FormError.Should().Be(SignUpDetailsPageModel.ServiceUnavailableMessage);
        pageModel.IsBusy.Should().BeFalse();
        pageModel.CreateAccountCommand.CanExecute(null).Should().BeTrue();
    }

    private static SignUpDetailsPageModel Create(Mock<IOnboardingClient> client, Mock<IOnboardingNavigator> navigator)
    {
        var pageModel = new SignUpDetailsPageModel(
            client.Object,
            navigator.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero)));
        pageModel.Initialize("tok", "+1 (555) ••• 9421");
        return pageModel;
    }

    private static SignUpDetailsPageModel CreateValid(Mock<IOnboardingClient> client, Mock<IOnboardingNavigator> navigator, string email)
    {
        var pageModel = Create(client, navigator);
        pageModel.FirstName = "Ada";
        pageModel.LastName = "Okafor";
        pageModel.Email = email;
        pageModel.Password = "secret1";
        pageModel.TermsAccepted = true;
        return pageModel;
    }
}
