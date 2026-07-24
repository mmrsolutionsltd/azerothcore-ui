using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/database-backups")]
public sealed class DatabaseBackupsController(
    DatabaseBackupService backupService,
    DatabaseBackupScheduler scheduler) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<DatabaseBackupSummary>> GetBackups() =>
        IsLocalRequest() ? Ok(backupService.GetBackups()) : NotFound();

    [HttpPost]
    public async Task<ActionResult<DatabaseBackupSummary>> CreateBackup(
        CreateDatabaseBackupRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm creating the database backup."));
        return Ok(await scheduler.RunNowAsync("Manual", cancellationToken));
    }

    [HttpGet("schedule")]
    public ActionResult<DatabaseBackupDashboard> GetSchedule() =>
        IsLocalRequest() ? Ok(scheduler.GetDashboard()) : NotFound();

    [HttpPut("schedule")]
    public ActionResult<DatabaseBackupDashboard> UpdateSchedule(DatabaseBackupSchedule schedule)
    {
        if (!IsLocalRequest()) return NotFound();
        return Ok(scheduler.UpdateSchedule(schedule));
    }

    [HttpPost("restore")]
    public async Task<ActionResult<AdministrationResult>> RestoreBackup(
        RestoreDatabaseBackupRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return NotFound();
        if (!request.Confirmed)
            return BadRequest(new AdministrationResult(false, "Confirm the database restore."));
        await backupService.RestoreAsync(request.BackupId, cancellationToken);
        return Ok(new AdministrationResult(
            true,
            "The database backup was restored. A verified pre-restore safety backup was created first."));
    }

    private bool IsLocalRequest() => HttpContext.Connection.RemoteIpAddress is { } address
        && IPAddress.IsLoopback(address);
}
