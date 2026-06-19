using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SessionDetailPage : ContentPage
{
    public SessionDetailPage(SessionDetailPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
