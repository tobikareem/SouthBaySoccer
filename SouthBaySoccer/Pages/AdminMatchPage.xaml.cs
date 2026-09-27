namespace SouthBaySoccer.Pages;

public partial class AdminMatchPage : ContentPage
{
    public AdminMatchPage(PageModels.AdminMatchPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
