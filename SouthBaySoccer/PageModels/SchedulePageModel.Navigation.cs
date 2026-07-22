using CommunityToolkit.Mvvm.Input;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Shell-touching navigation for <see cref="SchedulePageModel"/>, kept in a partial so the main
/// page model stays MAUI-free and unit-testable (same split as SessionDetailPageModel).
/// </summary>
public partial class SchedulePageModel
{
    [RelayCommand]
    private static Task GoBack() => Shell.Current.GoToAsync("..");
}
