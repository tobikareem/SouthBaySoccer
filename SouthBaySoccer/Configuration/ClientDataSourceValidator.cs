namespace SouthBaySoccer.Configuration;

public static class ClientDataSourceValidator
{
    public static void Validate(ClientDataSourceOptions options, bool seedProviderAvailable)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DataSource == ClientDataSource.Seed && !seedProviderAvailable)
        {
            throw new InvalidOperationException(
                "ClientDataSource 'Seed' is unavailable in Release builds.");
        }

    }
}


