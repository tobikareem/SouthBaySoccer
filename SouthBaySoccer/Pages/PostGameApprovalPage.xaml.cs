using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class PostGameApprovalPage : ContentPage
{
    public PostGameApprovalPage(PostGameApprovalPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }
}
