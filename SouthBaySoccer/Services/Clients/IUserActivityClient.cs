using SouthBaySoccer.Contracts.UserActivity;

namespace SouthBaySoccer.Services.Clients;

public interface IUserActivityClient
{
    Task<UserActivityPageDto> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
