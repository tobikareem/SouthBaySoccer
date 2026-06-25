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
}
