using System.Net;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/operations-dashboard")]
public sealed class OperationsDashboardController(
    OperationsDashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OperationsDashboard>> Get(
        CancellationToken cancellationToken) =>
        IsLocalRequest()
            ? Ok(await dashboardService.GetAsync(cancellationToken))
            : NotFound();

    [HttpPut("alerts")]
    public ActionResult<OperationsAlertSettings> UpdateAlerts(
        OperationsAlertSettings settings) =>
        IsLocalRequest()
            ? Ok(dashboardService.UpdateAlertSettings(settings))
            : NotFound();

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is { } address
        && IPAddress.IsLoopback(address);
}
