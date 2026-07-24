namespace AzerothCore_UI.Web.Models;

public sealed record AdministrationUserSummary(
    ulong Id, string Username, string Role, string AccountScope, bool Enabled, bool MustChangePassword,
    DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, DateTime? LockoutUntilUtc);
public sealed record AdministrationAuthenticationRequest(
    string Username, string Password, string? RemoteAddress);
public sealed record AdministrationAuthenticationResult(
    bool Succeeded, string Message, AdministrationUserIdentity? User);
public sealed record AdministrationUserIdentity(
    ulong Id, string Username, string Role, string AccountScope,
    IReadOnlyList<string> Permissions, IReadOnlyList<uint> GameAccountIds,
    bool MustChangePassword, string SecurityStamp);
public sealed record BootstrapAdministrationUserRequest(
    string Username, string Password, string? RemoteAddress);
public sealed record CreateAdministrationUserRequest(
    string Actor, string Username, string Password, string Role, string AccountScope,
    IReadOnlyList<uint> GameAccountIds, bool MustChangePassword);
public sealed record UpdateAdministrationUserRequest(
    string Actor, string Role, string AccountScope,
    IReadOnlyList<uint> GameAccountIds, bool Enabled);
public sealed record ResetAdministrationPasswordRequest(
    string Actor, string Password, bool MustChangePassword);
public sealed record ChangeAdministrationPasswordRequest(
    ulong UserId, string CurrentPassword, string NewPassword);
public sealed record AdministrationSessionValidationRequest(
    ulong UserId, string SecurityStamp);
public sealed record AdministrationAuditEntry(
    ulong Id, string Username, string Action, string Outcome, string? RemoteAddress,
    string? Detail, DateTime OccurredAtUtc);
public sealed record AdministrationPermission(
    string Key, string DisplayName, string Category, string Description);
public sealed record AdministrationRole(
    string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions);
public sealed record SaveAdministrationRoleRequest(
    string Actor, string Name, string Description, IReadOnlyList<string> Permissions);
public sealed record GameAccountOption(uint Id, string Username);
