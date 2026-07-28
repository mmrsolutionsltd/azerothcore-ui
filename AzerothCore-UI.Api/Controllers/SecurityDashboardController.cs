using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/security-dashboard")]
public sealed class SecurityDashboardController(SecurityDashboardService service)
    : ControllerBase
{
    [HttpGet]
    public Task<SecurityDashboard> Get(CancellationToken cancellationToken) =>
        service.GetAsync(cancellationToken);
}
