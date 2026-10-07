using SouthBaySoccer.Contracts.UserActivity;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedUserActivityClient : IUserActivityClient
{
    public Task<UserActivityPageDto> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new UserActivityPageDto([], page, pageSize, false, null));
}
