using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Pages;

namespace SouthBaySoccer.Services.Authentication;

/// <summary>
/// The onboarding pages live in the plain NavigationPage that hosts Welcome Back (Shell does not
/// exist before sign-in), so navigation is push/pop on that stack rather than Shell routes.
/// </summary>
public sealed class OnboardingNavigator(IServiceProvider services) : IOnboardingNavigator
{
    public Task ShowSignUpStartAsync(CancellationToken cancellationToken) =>
        PushAsync<SignUpStartPage>(_ => { }, cancellationToken);

    public Task ShowLinkWaitingAsync(OnboardingLinkKind kind, CancellationToken cancellationToken) =>
        PushAsync<LinkWaitingPage>(page => ((LinkWaitingPageModel)page.BindingContext).Initialize(kind), cancellationToken);

    public Task ShowSignUpExpiredAsync(OnboardingTokenFailure failure, CancellationToken cancellationToken) =>
        PushAsync<SignUpExpiredPage>(page => ((SignUpExpiredPageModel)page.BindingContext).Initialize(failure), cancellationToken);

    public Task ShowSignUpDetailsAsync(string token, string phoneMasked, CancellationToken cancellationToken) =>
        PushAsync<SignUpDetailsPage>(page => ((SignUpDetailsPageModel)page.BindingContext).Initialize(token, phoneMasked), cancellationToken);

    public Task ShowSignUpWelcomeAsync(RegistrationCompletedResponse registration, CancellationToken cancellationToken) =>
        PushAsync<SignUpWelcomePage>(page => ((SignUpWelcomePageModel)page.BindingContext).Initialize(registration), cancellationToken);

    public Task ShowSignInVerifyAsync(PhoneSignInStartResponse pendingSignIn, CancellationToken cancellationToken) =>
        PushAsync<SignInVerifyPage>(page => ((SignInVerifyPageModel)page.BindingContext).Initialize(pendingSignIn), cancellationToken);

    public Task PopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var navigation = RequireNavigation();
            if (navigation.NavigationStack.Count > 1)
            {
                await navigation.PopAsync();
            }
        });
    }

    public Task PopToWelcomeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MainThread.InvokeOnMainThreadAsync(() => RequireNavigation().PopToRootAsync());
    }

    private Task PushAsync<TPage>(Action<TPage> configure, CancellationToken cancellationToken)
        where TPage : Page
    {
        cancellationToken.ThrowIfCancellationRequested();
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var page = services.GetRequiredService<TPage>();
            configure(page);
            return RequireNavigation().PushAsync(page);
        });
    }

    private static INavigation RequireNavigation()
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        return root is NavigationPage navigationPage
            ? navigationPage.Navigation
            : throw new InvalidOperationException("Onboarding pages require the Welcome Back navigation stack.");
    }
}
