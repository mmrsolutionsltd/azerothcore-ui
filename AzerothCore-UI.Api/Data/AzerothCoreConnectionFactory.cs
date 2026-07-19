using MySqlConnector;

namespace AzerothCore_UI.Api.Data;

public sealed class AzerothCoreConnectionFactory(IConfiguration configuration)
{
    public MySqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("AzerothCore")
            ?? throw new InvalidOperationException(
                "Connection string 'AzerothCore' is not configured.");

        return new MySqlConnection(connectionString);
    }
}
