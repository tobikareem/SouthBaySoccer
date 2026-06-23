using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class ProfilePage : ContentPage
{
    public ProfilePage(ProfilePageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
