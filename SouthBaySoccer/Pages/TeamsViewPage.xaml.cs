namespace SouthBaySoccer.Pages;

public partial class TeamsViewPage : ContentPage
{
    public TeamsViewPage(PageModels.TeamsViewPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
