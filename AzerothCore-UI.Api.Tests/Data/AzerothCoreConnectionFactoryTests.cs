using AzerothCore_UI.Api.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Data;

public sealed class AzerothCoreConnectionFactoryTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void CreateConnection_DisablesImplicitSslForLoopbackServer(string server)
    {
        var connection = Create($"Server={server};User ID=test;Password=test");

        Assert.Equal(MySqlSslMode.None, new MySqlConnectionStringBuilder(connection.ConnectionString).SslMode);
    }

    [Fact]
    public void CreateConnection_PreservesExplicitLoopbackSslMode()
    {
        var connection = Create("Server=localhost;User ID=test;Password=test;SslMode=Required");

        Assert.Equal(MySqlSslMode.Required, new MySqlConnectionStringBuilder(connection.ConnectionString).SslMode);
    }

    [Fact]
    public void CreateConnection_DoesNotDisableSslForRemoteServer()
    {
        var connection = Create("Server=db.example.test;User ID=test;Password=test");

        Assert.NotEqual(MySqlSslMode.None, new MySqlConnectionStringBuilder(connection.ConnectionString).SslMode);
    }

    private static MySqlConnection Create(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AzerothCore"] = connectionString
            }).Build();
        return new AzerothCoreConnectionFactory(configuration).CreateConnection();
    }
}
