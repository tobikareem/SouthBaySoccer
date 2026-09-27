namespace SouthBaySoccer.Services.Clients;

public interface IAnnouncementsNavigator
{
    Task GoToAnnouncementsAsync(Guid groupId);
    Task GoToAdminBroadcastAsync();
    Task GoBackAsync();
}
