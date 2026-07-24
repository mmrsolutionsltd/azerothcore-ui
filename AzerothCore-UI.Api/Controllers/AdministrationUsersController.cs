using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/administration-users")]
public sealed class AdministrationUsersController(AdministrationAccountStore store) : ControllerBase
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
        try { return Ok(await store.CreateAsync(request)); }
        catch (Exception exception) when (
            exception is ArgumentException or MySqlConnector.MySqlException)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<AdministrationResult>> Update(
        ulong id, UpdateAdministrationUserRequest request)
    {
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
    public async Task<AdministrationResult> RevokeSessions(ulong id, [FromQuery] string actor)
    {
        await store.RevokeSessionsAsync(id, actor);
        return new(true, "All sessions revoked.");
    }

    [HttpGet("audit")]
    public Task<IReadOnlyList<AdministrationAuditEntry>> GetAudit() => store.GetAuditAsync();

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<AdministrationResult>> Delete(
        ulong id, [FromQuery] string actor)
    {
        try
        {
            await store.DeleteAsync(id, actor);
            return Ok(new AdministrationResult(true, "Administration user deleted."));
        }
        catch (InvalidOperationException exception)
        { return BadRequest(new AdministrationResult(false, exception.Message)); }
    }
}
