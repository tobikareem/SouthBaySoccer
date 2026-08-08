using CommunityToolkit.Mvvm.Input;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// MAUI/Shell-coupled half of <see cref="CreateSessionPageModel"/>: implements Shell back-navigation.
/// Kept in a separate partial so the behaviour half stays free of MAUI types and can be unit-tested in
/// the plain client test project.
/// </summary>
public partial class CreateSessionPageModel
{
    /// <summary>Navigates back to the previous Shell route.</summary>
    [RelayCommand]
    private static Task GoBack() => Shell.Current.GoToAsync("..");

    /// <summary>Opens the selected venue in the system maps app. Best-effort — never surfaces a crash.</summary>
    [RelayCommand]
    private async Task OpenVenueMap()
    {
        if (SelectedVenue is not { } venue || string.IsNullOrWhiteSpace(venue.Name))
        {
            return;
        }

        var query = string.IsNullOrWhiteSpace(venue.Locality)
            ? venue.Name
            : $"{venue.Name}, {venue.Locality}";
        var uri = new Uri(
            $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}");

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
