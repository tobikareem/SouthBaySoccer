using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class StatsPage : ContentPage
{
    public StatsPage(LeaderboardPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
