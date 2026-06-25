using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class CaptainAssignmentPage : ContentPage
{
    public CaptainAssignmentPage(CaptainAssignmentPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
