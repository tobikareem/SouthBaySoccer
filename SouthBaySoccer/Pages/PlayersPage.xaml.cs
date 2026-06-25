using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class PlayersPage : ContentPage
{
    public PlayersPage(PlayersPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
