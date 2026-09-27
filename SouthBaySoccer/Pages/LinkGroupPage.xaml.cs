using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class LinkGroupPage : ContentPage
{
    public LinkGroupPage(LinkGroupPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }

    // The link step is required: swallow the Android hardware back button so the player cannot back
    // out of it into an unauthenticated/unlinked state.
    protected override bool OnBackButtonPressed() => true;
}
