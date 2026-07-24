using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AzerothCore_UI.Web.Services;

public sealed class ApiReadinessHealthCheck(IHttpClientFactory clientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await clientFactory.CreateClient("ApiHealth")
                .GetAsync("health/ready", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("The private API is ready.")
                : HealthCheckResult.Unhealthy(
                    $"The private API returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "The private API is not reachable.", exception);
        }
    }
}
