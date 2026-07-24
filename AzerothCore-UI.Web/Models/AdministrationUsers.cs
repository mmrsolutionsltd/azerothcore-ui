namespace AzerothCore_UI.Web.Models;

public sealed record AdministrationUserSummary(
    ulong Id, string Username, string Role, bool Enabled, bool MustChangePassword,
    DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, DateTime? LockoutUntilUtc);
public sealed record AdministrationAuthenticationRequest(
    string Username, string Password, string? RemoteAddress);
public sealed record AdministrationAuthenticationResult(
    bool Succeeded, string Message, AdministrationUserIdentity? User);
public sealed record AdministrationUserIdentity(
    ulong Id, string Username, string Role, bool MustChangePassword, string SecurityStamp);
public sealed record BootstrapAdministrationUserRequest(
    string Username, string Password, string? RemoteAddress);
public sealed record CreateAdministrationUserRequest(
    string Actor, string Username, string Password, string Role, bool MustChangePassword);
public sealed record UpdateAdministrationUserRequest(
    string Actor, string Role, bool Enabled);
public sealed record ResetAdministrationPasswordRequest(
    string Actor, string Password, bool MustChangePassword);
public sealed record ChangeAdministrationPasswordRequest(
    ulong UserId, string CurrentPassword, string NewPassword);
public sealed record AdministrationSessionValidationRequest(
    ulong UserId, string SecurityStamp);
public sealed record AdministrationAuditEntry(
    ulong Id, string Username, string Action, string Outcome, string? RemoteAddress,
    string? Detail, DateTime OccurredAtUtc);
