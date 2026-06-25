namespace SouthBaySoccer.Services.Clients;

public interface ILeaderboardNavigator
{
    Task OpenPlayerProfileAsync(Guid playerId);
}
