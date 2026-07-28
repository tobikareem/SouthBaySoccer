using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class AnnouncementsPage : ContentPage
{
    public AnnouncementsPage(AnnouncementsPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
