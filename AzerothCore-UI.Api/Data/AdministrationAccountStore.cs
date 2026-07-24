using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Security;
using Dapper;
using MySqlConnector;

namespace AzerothCore_UI.Api.Data;

public sealed class AdministrationAccountStore(
    IConfiguration configuration,
    AdministrationPasswordHasher passwordHasher)
{
    private readonly string connectionString =
        configuration.GetConnectionString("AzerothCoreUi")
        ?? throw new InvalidOperationException(
            "Connection string 'AzerothCoreUi' is not configured.");

    private MySqlConnection Open() => new(connectionString);
    private static string Normalize(string username) => username.Trim().ToUpperInvariant();
    private static void ValidateUsername(string username)
    {
        if (username.Trim().Length is < 3 or > 64
            || username.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
            throw new ArgumentException(
                "Username must be 3-64 letters, numbers, dots, hyphens, or underscores.");
    }
    private static string ValidateRole(string role) =>
        role is "Owner" or "Administrator" ? role
        : throw new ArgumentException("Role must be Owner or Administrator.");

    public async Task<bool> HasUsersAsync()
    {
        await using var connection = Open();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM admin_user") > 0;
    }

    public async Task<AdministrationUserIdentity> BootstrapAsync(
        BootstrapAdministrationUserRequest request)
    {
        ValidateUsername(request.Username);
        AdministrationPasswordHasher.Validate(request.Password);
        await using var connection = Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        if (await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM admin_user FOR UPDATE", transaction: transaction) != 0)
            throw new InvalidOperationException("The owner account has already been created.");
        var now = DateTime.UtcNow;
        var stamp = Guid.NewGuid().ToString();
        var id = await connection.ExecuteScalarAsync<ulong>("""
            INSERT INTO admin_user
              (username, normalized_username, password_hash, role, enabled,
               must_change_password, security_stamp, created_at_utc)
            VALUES (@Username, @Normalized, @Hash, 'Owner', 1, 0, @Stamp, @Now);
            SELECT LAST_INSERT_ID();
            """, new {
                Username = request.Username.Trim(), Normalized = Normalize(request.Username),
                Hash = passwordHasher.Hash(request.Password), Stamp = stamp, Now = now
            }, transaction);
        await AuditAsync(connection, transaction, id, request.Username.Trim(),
            "OwnerBootstrap", "Succeeded", request.RemoteAddress, null);
        await transaction.CommitAsync();
        return new(id, request.Username.Trim(), "Owner", false, stamp);
    }

    public async Task<AdministrationAuthenticationResult> AuthenticateAsync(
        AdministrationAuthenticationRequest request)
    {
        await using var connection = Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var user = await connection.QuerySingleOrDefaultAsync<UserRow>("""
            SELECT id, username, password_hash PasswordHash, role, enabled,
                   must_change_password MustChangePassword,
                   failed_login_count FailedLoginCount,
                   lockout_until_utc LockoutUntilUtc, security_stamp SecurityStamp
            FROM admin_user WHERE normalized_username=@Normalized FOR UPDATE
            """, new { Normalized = Normalize(request.Username) }, transaction);
        var now = DateTime.UtcNow;
        if (user is null || !user.Enabled)
        {
            await AuditAsync(connection, transaction, user?.Id, request.Username.Trim(),
                "Login", "Failed", request.RemoteAddress, "Unknown or disabled account.");
            await transaction.CommitAsync();
            return new(false, "The username or password was not accepted.", null);
        }
        if (user.LockoutUntilUtc > now)
        {
            await AuditAsync(connection, transaction, user.Id, user.Username,
                "Login", "LockedOut", request.RemoteAddress, null);
            await transaction.CommitAsync();
            return new(false, "The account is temporarily locked.", null);
        }
        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            var attempts = user.FailedLoginCount + 1;
            DateTime? lockedUntil = attempts >= 5 ? now.AddMinutes(15) : null;
            await connection.ExecuteAsync("""
                UPDATE admin_user SET failed_login_count=@Attempts,
                  lockout_until_utc=@LockedUntil WHERE id=@Id
                """, new { Attempts = attempts >= 5 ? 0 : attempts, LockedUntil = lockedUntil, user.Id },
                transaction);
            await AuditAsync(connection, transaction, user.Id, user.Username,
                "Login", "Failed", request.RemoteAddress, null);
            await transaction.CommitAsync();
            return new(false, "The username or password was not accepted.", null);
        }
        await connection.ExecuteAsync("""
            UPDATE admin_user SET failed_login_count=0, lockout_until_utc=NULL,
              last_login_at_utc=@Now WHERE id=@Id
            """, new { Now = now, user.Id }, transaction);
        await AuditAsync(connection, transaction, user.Id, user.Username,
            "Login", "Succeeded", request.RemoteAddress, null);
        await transaction.CommitAsync();
        return new(true, "Signed in.",
            new(user.Id, user.Username, user.Role, user.MustChangePassword,
                user.SecurityStamp.ToString()));
    }

    public async Task<bool> ValidateSessionAsync(AdministrationSessionValidationRequest request)
    {
        await using var connection = Open();
        return await connection.ExecuteScalarAsync<long>("""
            SELECT COUNT(*) FROM admin_user
            WHERE id=@UserId AND enabled=1 AND security_stamp=@SecurityStamp
            """, request) == 1;
    }

    public async Task<IReadOnlyList<AdministrationUserSummary>> GetUsersAsync()
    {
        await using var connection = Open();
        return (await connection.QueryAsync<AdministrationUserSummary>("""
            SELECT id, username, role, enabled,
              must_change_password MustChangePassword, created_at_utc CreatedAtUtc,
              last_login_at_utc LastLoginAtUtc, lockout_until_utc LockoutUntilUtc
            FROM admin_user ORDER BY username
            """)).AsList();
    }

    public async Task<AdministrationUserSummary> CreateAsync(
        CreateAdministrationUserRequest request)
    {
        ValidateUsername(request.Username);
        AdministrationPasswordHasher.Validate(request.Password);
        var role = ValidateRole(request.Role);
        await using var connection = Open();
        var now = DateTime.UtcNow;
        var id = await connection.ExecuteScalarAsync<ulong>("""
            INSERT INTO admin_user
              (username, normalized_username, password_hash, role, enabled,
               must_change_password, security_stamp, created_at_utc)
            VALUES (@Username, @Normalized, @Hash, @Role, 1, @MustChange, @Stamp, @Now);
            SELECT LAST_INSERT_ID();
            """, new {
                Username = request.Username.Trim(), Normalized = Normalize(request.Username),
                Hash = passwordHasher.Hash(request.Password), Role = role,
                MustChange = request.MustChangePassword, Stamp = Guid.NewGuid().ToString(), Now = now
            });
        await WriteAuditAsync(id, request.Username.Trim(), "UserCreated", "Succeeded", request.Actor);
        return new(id, request.Username.Trim(), role, true, request.MustChangePassword, now, null, null);
    }

    public async Task UpdateAsync(ulong id, UpdateAdministrationUserRequest request)
    {
        var role = ValidateRole(request.Role);
        await using var connection = Open();
        var target = await connection.QuerySingleAsync<(string Username, string Role)>(
            "SELECT username, role FROM admin_user WHERE id=@id", new { id });
        if (target.Role == "Owner" && (role != "Owner" || !request.Enabled)
            && await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM admin_user WHERE role='Owner' AND enabled=1") <= 1)
            throw new InvalidOperationException("The final enabled Owner cannot be demoted or disabled.");
        await connection.ExecuteAsync("""
            UPDATE admin_user SET role=@role, enabled=@Enabled,
              security_stamp=@Stamp WHERE id=@id
            """, new { id, role, request.Enabled, Stamp = Guid.NewGuid().ToString() });
        await WriteAuditAsync(id, target.Username, "UserUpdated", "Succeeded", request.Actor);
    }

    public async Task ResetPasswordAsync(ulong id, ResetAdministrationPasswordRequest request)
    {
        AdministrationPasswordHasher.Validate(request.Password);
        await using var connection = Open();
        var username = await connection.ExecuteScalarAsync<string>(
            "SELECT username FROM admin_user WHERE id=@id", new { id })
            ?? throw new KeyNotFoundException("Administration user not found.");
        await connection.ExecuteAsync("""
            UPDATE admin_user SET password_hash=@Hash,
              must_change_password=@MustChangePassword, failed_login_count=0,
              lockout_until_utc=NULL, security_stamp=@Stamp WHERE id=@id
            """, new { id, Hash = passwordHasher.Hash(request.Password),
                request.MustChangePassword, Stamp = Guid.NewGuid().ToString() });
        await WriteAuditAsync(id, username, "PasswordReset", "Succeeded", request.Actor);
    }

    public async Task ChangePasswordAsync(ChangeAdministrationPasswordRequest request)
    {
        AdministrationPasswordHasher.Validate(request.NewPassword);
        await using var connection = Open();
        var row = await connection.QuerySingleAsync<(string Username, string PasswordHash)>("""
            SELECT username, password_hash PasswordHash FROM admin_user WHERE id=@UserId
            """, request);
        if (!passwordHasher.Verify(request.CurrentPassword, row.PasswordHash))
            throw new InvalidOperationException("The current password was not accepted.");
        await connection.ExecuteAsync("""
            UPDATE admin_user SET password_hash=@Hash, must_change_password=0,
              security_stamp=@Stamp WHERE id=@UserId
            """, new { request.UserId, Hash = passwordHasher.Hash(request.NewPassword),
                Stamp = Guid.NewGuid().ToString() });
        await WriteAuditAsync(request.UserId, row.Username, "PasswordChanged", "Succeeded", row.Username);
    }

    public async Task RevokeSessionsAsync(ulong id, string actor)
    {
        await using var connection = Open();
        var username = await connection.ExecuteScalarAsync<string>(
            "SELECT username FROM admin_user WHERE id=@id", new { id })
            ?? throw new KeyNotFoundException("Administration user not found.");
        await connection.ExecuteAsync(
            "UPDATE admin_user SET security_stamp=@Stamp WHERE id=@id",
            new { id, Stamp = Guid.NewGuid().ToString() });
        await WriteAuditAsync(id, username, "SessionsRevoked", "Succeeded", actor);
    }

    public async Task DeleteAsync(ulong id, string actor)
    {
        await using var connection = Open();
        var target = await connection.QuerySingleAsync<(string Username, string Role)>(
            "SELECT username, role FROM admin_user WHERE id=@id", new { id });
        if (target.Role == "Owner" && await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM admin_user WHERE role='Owner' AND enabled=1") <= 1)
            throw new InvalidOperationException("The final enabled Owner cannot be deleted.");
        await WriteAuditAsync(id, target.Username, "UserDeleted", "Succeeded", actor);
        await connection.ExecuteAsync("DELETE FROM admin_user WHERE id=@id", new { id });
    }

    public async Task<IReadOnlyList<AdministrationAuditEntry>> GetAuditAsync()
    {
        await using var connection = Open();
        return (await connection.QueryAsync<AdministrationAuditEntry>("""
            SELECT id, username, action, outcome, remote_address RemoteAddress,
              detail, occurred_at_utc OccurredAtUtc
            FROM admin_audit_log ORDER BY occurred_at_utc DESC LIMIT 200
            """)).AsList();
    }

    private async Task WriteAuditAsync(
        ulong? userId, string username, string action, string outcome, string? detail)
    {
        await using var connection = Open();
        await AuditAsync(connection, null, userId, username, action, outcome, null, detail);
    }
    private static Task AuditAsync(
        MySqlConnection connection, MySqlTransaction? transaction, ulong? userId,
        string username, string action, string outcome, string? remoteAddress, string? detail) =>
        connection.ExecuteAsync("""
            INSERT INTO admin_audit_log
              (user_id, username, action, outcome, remote_address, detail, occurred_at_utc)
            VALUES (@userId, @username, @action, @outcome, @remoteAddress, @detail, @Now)
            """, new { userId, username, action, outcome, remoteAddress, detail, Now = DateTime.UtcNow },
            transaction);

    private sealed class UserRow
    {
        public ulong Id { get; init; }
        public string Username { get; init; } = "";
        public string PasswordHash { get; init; } = "";
        public string Role { get; init; } = "";
        public bool Enabled { get; init; }
        public bool MustChangePassword { get; init; }
        public uint FailedLoginCount { get; init; }
        public DateTime? LockoutUntilUtc { get; init; }
        public Guid SecurityStamp { get; init; }
    }
}
