using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Navigation;

public sealed class ShellAnnouncementsNavigator : IAnnouncementsNavigator
{
    public Task GoToAnnouncementsAsync(Guid groupId) =>
        Shell.Current.GoToAsync($"announcements?groupId={groupId}");

    public Task GoToAdminBroadcastAsync(Guid? groupId = null) =>
        Shell.Current.GoToAsync(groupId is null ? "admin-broadcast" : $"admin-broadcast?groupId={groupId}");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
