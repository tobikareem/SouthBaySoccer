using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class MatchStatsPage : ContentPage
{
    public MatchStatsPage(MatchStatsPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
