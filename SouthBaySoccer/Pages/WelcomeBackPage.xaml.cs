using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class WelcomeBackPage : ContentPage
{
    public WelcomeBackPage(WelcomeBackPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
