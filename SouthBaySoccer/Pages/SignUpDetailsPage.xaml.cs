using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SignUpDetailsPage : ContentPage
{
    public SignUpDetailsPage(SignUpDetailsPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
