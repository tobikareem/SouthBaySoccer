using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SignUpExpiredPage : ContentPage
{
    public SignUpExpiredPage(SignUpExpiredPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
