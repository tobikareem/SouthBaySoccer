using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class CreateSessionPage : ContentPage
{
    public CreateSessionPage(CreateSessionPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
