namespace SouthBaySoccer.Configuration;

public enum ClientDataSource
{
    Seed,
    Api
}

public sealed class ClientDataSourceOptions
{
    public ClientDataSource DataSource { get; init; } = ClientDataSource.Seed;

    public static ClientDataSourceOptions FromValue(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return new ClientDataSourceOptions();
        }

        if (!Enum.TryParse<ClientDataSource>(
                configuredValue,
                ignoreCase: true,
                out var dataSource))
        {
            throw new InvalidOperationException(
                $"ClientDataSource '{configuredValue}' is invalid. Expected Seed or Api.");
        }

        return new ClientDataSourceOptions { DataSource = dataSource };
    }
}
