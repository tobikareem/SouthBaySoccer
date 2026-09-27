using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SignInVerifyPage : ContentPage
{
    public SignInVerifyPage(SignInVerifyPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
