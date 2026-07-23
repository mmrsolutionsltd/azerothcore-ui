using System.Text;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(AzerothCoreDiagnosticsService diagnosticsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DiagnosticsDashboard>> Get(CancellationToken cancellationToken) =>
        IsLocalRequest() ? Ok(await diagnosticsService.GetAsync(cancellationToken)) : NotFound();

    [HttpGet("report")]
    public async Task<IActionResult> Report(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        var report = DiagnosticsReportBuilder.Build(await diagnosticsService.GetAsync(cancellationToken));
        return File(Encoding.UTF8.GetBytes(report), "text/plain; charset=utf-8",
            $"azerothcore-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
    }

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is null
        || System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress);
}
