using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Configuration;

public static class ClientDataSourceValidator
{
    private static readonly string[] MissingApiRegistrations =
    [
        "IAuthenticationClient",
        nameof(ISessionsClient),
        nameof(ISessionAdminClient),
        nameof(IRosterClient),
        nameof(IStatsClient),
        nameof(ILeaderboardClient),
        nameof(IProfileClient)
    ];

    public static void Validate(ClientDataSourceOptions options, bool seedProviderAvailable)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DataSource == ClientDataSource.Seed && !seedProviderAvailable)
        {
            throw new InvalidOperationException(
                "ClientDataSource 'Seed' is unavailable in Release builds.");
        }

        if (options.DataSource == ClientDataSource.Api)
        {
            throw new InvalidOperationException(
                $"ClientDataSource 'Api' is unavailable. Missing registrations: {string.Join(", ", MissingApiRegistrations)}.");
        }
    }
}
