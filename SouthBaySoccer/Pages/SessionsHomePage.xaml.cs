using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SessionsHomePage : ContentPage
{
    public SessionsHomePage(SessionsHomePageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
