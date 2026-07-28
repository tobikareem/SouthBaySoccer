using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Navigation;

public sealed class ShellAnnouncementsNavigator : IAnnouncementsNavigator
{
    public Task GoToAnnouncementsAsync(Guid groupId) =>
        Shell.Current.GoToAsync($"announcements?groupId={groupId}");

    public Task GoToAdminBroadcastAsync() =>
        Shell.Current.GoToAsync("admin-broadcast");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
