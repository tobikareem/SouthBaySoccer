using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class UserActivityPage : ContentPage
{
    public UserActivityPage(UserActivityPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
