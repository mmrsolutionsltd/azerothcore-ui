using MySqlConnector;
using System.Net;

namespace AzerothCore_UI.Api.Data;

public sealed class AzerothCoreConnectionFactory(IConfiguration configuration)
{
    public MySqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("AzerothCore")
            ?? throw new InvalidOperationException(
                "Connection string 'AzerothCore' is not configured.");

        var builder = new MySqlConnectionStringBuilder(connectionString);
        var loopback = builder.Server.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(builder.Server, out var address) && IPAddress.IsLoopback(address));
        if (loopback && !connectionString.Contains("SslMode", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase))
            builder.SslMode = MySqlSslMode.None;

        return new MySqlConnection(builder.ConnectionString);
    }
}
