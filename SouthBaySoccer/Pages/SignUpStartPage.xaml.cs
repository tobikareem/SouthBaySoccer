using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SignUpStartPage : ContentPage
{
    public SignUpStartPage(SignUpStartPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
