using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class GameDayPage : ContentPage
{
    public GameDayPage(GameDayPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
