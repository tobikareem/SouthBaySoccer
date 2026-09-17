using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class GroupsMinePage : ContentPage
{
    public GroupsMinePage(GroupsMinePageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
