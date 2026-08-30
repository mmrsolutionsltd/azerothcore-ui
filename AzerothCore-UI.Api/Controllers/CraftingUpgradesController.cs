using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Security;
using AzerothCore_UI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/crafting-upgrades")]
public sealed class CraftingUpgradesController(CraftingUpgradeService upgrades) : ControllerBase
{
    [HttpGet("{targetGuid:long}")]
    public async Task<ActionResult<CraftingUpgradePlan>> Get(
        long targetGuid, int maximumSkillGap = 75, int futureLevelHorizon = 5,
        bool includeSidegrades = false, CancellationToken cancellationToken = default)
    {
        if (targetGuid is < 0 or > uint.MaxValue)
            return BadRequest("The character GUID is outside the supported range.");

        var identity = HttpContext.AdministrationIdentity();
        var plan = await upgrades.GetAsync((uint)targetGuid,
            identity?.AccountScope == "All", identity?.GameAccountIds ?? [],
            maximumSkillGap, futureLevelHorizon, includeSidegrades,
            cancellationToken);
        return plan is null
            ? NotFound("That character is not available in your account scope.")
            : Ok(plan);
    }
}
