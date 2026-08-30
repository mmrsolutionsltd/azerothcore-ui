using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Security;
using AzerothCore_UI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/realm-roster")]
public sealed class RealmRosterController(
    RealmRosterService roster,
    CompanionPartySessionStore companionParties) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RealmRosterSnapshot>> Get(
        [FromQuery] bool includeBots, CancellationToken cancellationToken)
    {
        var identity = HttpContext.AdministrationIdentity();
        return Ok(await roster.GetAsync(identity?.AccountScope == "All",
            identity?.GameAccountIds ?? [], identity?.Username,
            includeBots, cancellationToken));
    }

    [HttpPut("companion-party/timeout")]
    public async Task<ActionResult<AdministrationResult>> SetTimeout(
        CompanionPartyTimeoutRequest request, CancellationToken cancellationToken)
    {
        if (request.OfflineTimeoutMinutes is < 1 or > 120)
            return BadRequest(new AdministrationResult(false,
                "Offline retention must be between 1 and 120 minutes."));
        await companionParties.SetTimeoutAsync(request.LeaderName,
            request.OfflineTimeoutMinutes, cancellationToken);
        return Ok(new AdministrationResult(true,
            $"{request.LeaderName}'s party will be forgotten after " +
            $"{request.OfflineTimeoutMinutes} offline minute(s)."));
    }
}
