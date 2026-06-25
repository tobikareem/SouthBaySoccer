using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class TeamDraftPage : ContentPage
{
    public TeamDraftPage(TeamDraftPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
