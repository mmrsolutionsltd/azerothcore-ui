using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Microsoft.AspNetCore.Mvc;
using AzerothCore_UI.Api.Security;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/administration-users")]
public sealed class AdministrationUsersController(
    AdministrationAccountStore store,
    AzerothCoreConnectionFactory connections) : ControllerBase
{
    [HttpGet("state")]
    public async Task<object> GetState() => new { HasUsers = await store.HasUsersAsync() };

    [HttpPost("bootstrap")]
    public async Task<ActionResult<AdministrationUserIdentity>> Bootstrap(
        BootstrapAdministrationUserRequest request)
    {
        try { return Ok(await store.BootstrapAsync(request)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpPost("authenticate")]
    public Task<AdministrationAuthenticationResult> Authenticate(
        AdministrationAuthenticationRequest request) => store.AuthenticateAsync(request);

    [HttpPost("validate-session")]
    public Task<bool> ValidateSession(AdministrationSessionValidationRequest request) =>
        store.ValidateSessionAsync(request);

    [HttpGet]
    public Task<IReadOnlyList<AdministrationUserSummary>> GetUsers() => store.GetUsersAsync();

    [HttpPost]
    public async Task<ActionResult<AdministrationUserSummary>> Create(
        CreateAdministrationUserRequest request)
    {
        if (!await CanGrantRoleAsync(request.Role)
            || !CanGrantScope(request.AccountScope, request.GameAccountIds))
            return Forbid();
        try { return Ok(await store.CreateAsync(request)); }
        catch (Exception exception) when (
            exception is ArgumentException or MySqlConnector.MySqlException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<AdministrationResult>> Update(
        ulong id, UpdateAdministrationUserRequest request)
    {
        if (!await CanManageUserAsync(id) || !await CanGrantRoleAsync(request.Role)
            || !CanGrantScope(request.AccountScope, request.GameAccountIds))
            return Forbid();
        try
        {
            await store.UpdateAsync(id, request);
            return Ok(new AdministrationResult(true, "Administration user updated."));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpPost("{id:long}/reset-password")]
    public async Task<ActionResult<AdministrationResult>> ResetPassword(
        ulong id, ResetAdministrationPasswordRequest request)
    {
        if (!await CanManageUserAsync(id)) return Forbid();
        try
        {
            await store.ResetPasswordAsync(id, request);
            return Ok(new AdministrationResult(true, "Password reset and sessions revoked."));
        }
        catch (Exception exception) when (
            exception is ArgumentException or KeyNotFoundException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<AdministrationResult>> ChangePassword(
        ChangeAdministrationPasswordRequest request)
    {
        try
        {
            await store.ChangePasswordAsync(request);
            return Ok(new AdministrationResult(true, "Password changed."));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpPost("{id:long}/revoke-sessions")]
    public async Task<ActionResult<AdministrationResult>> RevokeSessions(
        ulong id, [FromQuery] string actor)
    {
        if (!await CanManageUserAsync(id)) return Forbid();
        await store.RevokeSessionsAsync(id, actor);
        return Ok(new AdministrationResult(true, "All sessions revoked."));
    }

    [HttpGet("audit")]
    public Task<IReadOnlyList<AdministrationAuditEntry>> GetAudit() => store.GetAuditAsync();

    [HttpGet("permissions")]
    public Task<IReadOnlyList<AdministrationPermission>> GetPermissions() =>
        store.GetPermissionsAsync();

    [HttpGet("{id:long}/game-accounts")]
    public Task<IReadOnlyList<uint>> GetUserGameAccounts(ulong id) =>
        store.GetUserGameAccountsAsync(id);

    [HttpGet("roles")]
    public Task<IReadOnlyList<AdministrationRole>> GetRoles() => store.GetRolesAsync();

    [HttpPut("roles/{name}")]
    public async Task<ActionResult<AdministrationResult>> SaveRole(
        string name, SaveAdministrationRoleRequest request)
    {
        if (!name.Equals(request.Name, StringComparison.Ordinal))
            return BadRequest(new AdministrationResult(false, "Role name does not match the route."));
        var actor = HttpContext.AdministrationIdentity();
        if (actor?.Role != "Owner"
            && request.Permissions.Any(permission => !actor!.Permissions.Contains(permission)))
            return Forbid();
        try
        {
            await store.SaveRoleAsync(request);
            return Ok(new AdministrationResult(true, "Role saved and affected sessions revoked."));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpDelete("roles/{name}")]
    public async Task<ActionResult<AdministrationResult>> DeleteRole(
        string name, [FromQuery] string actor)
    {
        try
        {
            await store.DeleteRoleAsync(name, actor);
            return Ok(new AdministrationResult(true, "Role deleted."));
        }
        catch (InvalidOperationException exception)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpGet("game-accounts")]
    public async Task<IReadOnlyList<GameAccountOption>> GetGameAccounts()
    {
        var identity = HttpContext.AdministrationIdentity();
        await using var connection = connections.CreateConnection();
        var rows = await Dapper.SqlMapper.QueryAsync<GameAccountOption>(connection, """
            SELECT id, username FROM acore_auth.account
            WHERE @AllAccounts OR id IN @AllowedAccounts
            ORDER BY username
            """, new {
                AllAccounts = identity?.AccountScope == "All",
                AllowedAccounts = identity?.GameAccountIds ?? []
            });
        return rows.ToArray();
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<AdministrationResult>> Delete(
        ulong id, [FromQuery] string actor)
    {
        if (!await CanManageUserAsync(id)) return Forbid();
        try
        {
            await store.DeleteAsync(id, actor);
            return Ok(new AdministrationResult(true, "Administration user deleted."));
        }
        catch (InvalidOperationException exception)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    private async Task<bool> CanGrantRoleAsync(string role)
    {
        var actor = HttpContext.AdministrationIdentity();
        if (actor?.Role == "Owner") return true;
        if (actor is null) return false;
        var permissions = await store.GetRolePermissionsAsync(role);
        return permissions.All(actor.Permissions.Contains);
    }

    private async Task<bool> CanManageUserAsync(ulong id)
    {
        var actor = HttpContext.AdministrationIdentity();
        if (actor?.Role == "Owner") return true;
        var target = await store.GetIdentityAsync(id);
        return actor is not null && target?.Role != "Owner";
    }

    private bool CanGrantScope(string scope, IReadOnlyList<uint> accountIds)
    {
        var actor = HttpContext.AdministrationIdentity();
        if (actor?.Role == "Owner" || actor?.AccountScope == "All") return true;
        if (actor is null || scope == "All") return false;
        if (scope == "None") return true;
        return actor.AccountScope == "Assigned"
            && accountIds.All(actor.GameAccountIds.Contains);
    }
}
