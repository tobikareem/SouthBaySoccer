using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Models;

namespace SouthBaySoccer.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}