using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SignUpWelcomePage : ContentPage
{
    public SignUpWelcomePage(SignUpWelcomePageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }

    // The account already exists and is linked; backing out here would discard the issued tokens
    // and force a full re-verification. Continue is the only way forward.
    protected override bool OnBackButtonPressed() => true;
}
