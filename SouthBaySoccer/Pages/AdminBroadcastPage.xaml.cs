using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class AdminBroadcastPage : ContentPage
{
    public AdminBroadcastPage(AdminBroadcastPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
