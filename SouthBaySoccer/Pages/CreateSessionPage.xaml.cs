using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class CreateSessionPage : ContentPage
{
    public CreateSessionPage(CreateSessionPageModel pageModel)
    {
        InitializeComponent();
        BindingContext = pageModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is CreateSessionPageModel pageModel
            && pageModel.LoadCommand.CanExecute(null))
        {
            pageModel.LoadCommand.ExecuteAsync(null).FireAndForgetSafeAsync();
        }
    }

    private void OnVenueSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (BindingContext is CreateSessionPageModel pageModel
            && pageModel.SearchVenuesCommand.CanExecute(e.NewTextValue))
        {
            pageModel.SearchVenuesCommand.ExecuteAsync(e.NewTextValue).FireAndForgetSafeAsync();
        }
    }
}

