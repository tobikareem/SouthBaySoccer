using CommunityToolkit.Mvvm.Input;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// MAUI/Shell-coupled half of <see cref="SessionDetailPageModel"/>: implements
/// <see cref="IQueryAttributable"/>, Shell back-navigation, and opening the venue map. Kept in a
/// separate partial so the behaviour half stays free of MAUI types and can be unit-tested in the plain
/// client test project.
/// </summary>
public partial class SessionDetailPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ApplyQueryAttributes(query);
        LoadCommand.Execute(null);
    }

    /// <summary>Navigates back to the previous Shell route.</summary>
    [RelayCommand]
    private static Task GoBack() => Shell.Current.GoToAsync("..");

    /// <summary>Opens the venue in the system maps app. Best-effort — never surfaces a crash.</summary>
    [RelayCommand]
    private async Task OpenMap()
    {
        if (!HasMap)
        {
            return;
        }

        var uri = new Uri(
            $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(Venue)}");

        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // Opening the map is a convenience; a launcher failure must not crash the screen.
        }
    }
}
