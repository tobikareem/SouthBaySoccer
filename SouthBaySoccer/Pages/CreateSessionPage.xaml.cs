using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages;

public partial class CreateSessionPage : ContentPage
{
    private const int VenueSearchDebounceMilliseconds = 300;

    private CancellationTokenSource? _venueSearchDebounceCts;
    private string? _latestVenueQuery;
    private bool _isVenueSearchRunning;
    private bool _isDisappearing;

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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _isDisappearing = true;
        _venueSearchDebounceCts?.Cancel();
        _venueSearchDebounceCts?.Dispose();
        _venueSearchDebounceCts = null;
    }

    private void OnVenueSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (BindingContext is not CreateSessionPageModel pageModel)
        {
            return;
        }

        _latestVenueQuery = e.NewTextValue;

        // Reset the debounce window on every keystroke; only the last keystroke within the window
        // starts a search. A search already in flight from an earlier cycle is not interrupted here -
        // see RunVenueSearchLoopAsync, which keeps re-searching until it catches up to the latest text.
        _venueSearchDebounceCts?.Cancel();
        _venueSearchDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _venueSearchDebounceCts = cts;

        DebounceVenueSearchAsync(pageModel, cts.Token).FireAndForgetSafeAsync();
    }

    private async Task DebounceVenueSearchAsync(CreateSessionPageModel pageModel, CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(VenueSearchDebounceMilliseconds, debounceToken);
        }
        catch (TaskCanceledException)
        {
            // A newer keystroke superseded this debounce cycle before the window elapsed.
            return;
        }

        if (_isDisappearing)
        {
            return;
        }

        await RunVenueSearchLoopAsync(pageModel);
    }

    /// <summary>
    /// Runs the venue search for the latest typed text. <c>SearchVenuesCommand</c> does not allow
    /// concurrent executions, so if a search from an earlier debounce cycle is still running, this call
    /// simply returns - that in-flight loop re-checks the latest text once its current request
    /// completes and keeps searching until the executed query matches what is currently typed, so a
    /// keystroke that lands mid-search is never silently dropped.
    /// </summary>
    private async Task RunVenueSearchLoopAsync(CreateSessionPageModel pageModel)
    {
        if (_isVenueSearchRunning)
        {
            return;
        }

        _isVenueSearchRunning = true;
        try
        {
            string? executedQuery;
            do
            {
                executedQuery = _latestVenueQuery;
                if (pageModel.SearchVenuesCommand.CanExecute(executedQuery))
                {
                    await pageModel.SearchVenuesCommand.ExecuteAsync(executedQuery);
                }
            }
            while (!_isDisappearing && !string.Equals(executedQuery, _latestVenueQuery, StringComparison.Ordinal));
        }
        finally
        {
            _isVenueSearchRunning = false;
        }
    }
}
