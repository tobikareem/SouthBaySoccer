using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class SchedulePage : ContentPage
{
    public SchedulePage(SchedulePageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
