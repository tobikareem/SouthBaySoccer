namespace SouthBaySoccer.Pages;

public partial class ClaimSpotPage : ContentPage
{
    public ClaimSpotPage(PageModels.ClaimSpotPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
