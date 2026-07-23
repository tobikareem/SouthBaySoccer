namespace SouthBaySoccer.Pages;

public partial class RecentGamesPage : ContentPage
{
    public RecentGamesPage(PageModels.RecentGamesPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
