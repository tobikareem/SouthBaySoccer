using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class LinkWaitingPage : ContentPage
{
    public LinkWaitingPage(LinkWaitingPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
