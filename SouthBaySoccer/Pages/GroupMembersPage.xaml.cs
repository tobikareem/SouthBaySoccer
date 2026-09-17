using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class GroupMembersPage : ContentPage
{
    public GroupMembersPage(GroupMembersPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
