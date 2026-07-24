using AzerothCore_UI.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AzerothCore_UI.Api.Services;

public sealed class ApiReadinessHealthCheck(
    AzerothCoreConnectionFactory coreConnections,
    AdministrationAccountStore administrationAccounts) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = coreConnections.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            if (!await administrationAccounts.HasUsersAsync())
                return HealthCheckResult.Degraded("The initial Owner account has not been created.");
            return HealthCheckResult.Healthy("Core and administration databases are reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "A required database is not reachable.", exception);
        }
    }
}
