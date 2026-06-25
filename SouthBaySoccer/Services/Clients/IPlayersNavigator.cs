namespace SouthBaySoccer.Services.Clients;

public interface IPlayersNavigator
{
    Task OpenPlayerProfileAsync(Guid playerId);
}
