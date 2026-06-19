using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Sessions;

namespace SouthBaySoccer.Services.Clients;

public interface ISessionsClient
{
    Task<SessionsDashboardDto> GetDashboardAsync(CancellationToken cancellationToken);

    Task<SessionDetailDto?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<ClientCommandResult> JoinWaitlistAsync(Guid sessionId, CancellationToken cancellationToken);
}
