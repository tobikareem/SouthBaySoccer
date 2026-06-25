using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class RateTeammatesPage : ContentPage
{
    public RateTeammatesPage(RateTeammatesPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
