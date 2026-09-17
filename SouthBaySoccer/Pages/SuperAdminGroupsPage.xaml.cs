using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SuperAdminGroupsPage : ContentPage
{
    public SuperAdminGroupsPage(SuperAdminGroupsPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
