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
    private static string ValidateScope(string scope) =>
        scope is "All" or "Assigned" or "None" ? scope
        : throw new ArgumentException("Account scope must be All, Assigned, or None.");

    private static async Task ValidateRoleAsync(
        MySqlConnection connection, string role, MySqlTransaction? transaction = null)
    {
        if (string.IsNullOrWhiteSpace(role) || await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM admin_role WHERE name=@role",
            new { role }, transaction) != 1)
            throw new ArgumentException("The selected role does not exist.");
    }

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
        var identity = await GetIdentityAsync(connection, id, transaction);
        await transaction.CommitAsync();
        return identity;
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
        var identity = await GetIdentityAsync(connection, user.Id, transaction);
        await transaction.CommitAsync();
        return new(true, "Signed in.", identity);
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
            SELECT id, username, role, account_scope AccountScope, enabled,
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
        var scope = ValidateScope(request.AccountScope);
        await using var connection = Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ValidateRoleAsync(connection, request.Role, transaction);
        var now = DateTime.UtcNow;
        var id = await connection.ExecuteScalarAsync<ulong>("""
            INSERT INTO admin_user
              (username, normalized_username, password_hash, role, enabled,
               must_change_password, security_stamp, created_at_utc, account_scope)
            VALUES (@Username, @Normalized, @Hash, @Role, 1, @MustChange, @Stamp, @Now, @Scope);
            SELECT LAST_INSERT_ID();
            """, new {
                Username = request.Username.Trim(), Normalized = Normalize(request.Username),
                Hash = passwordHasher.Hash(request.Password), request.Role,
                MustChange = request.MustChangePassword, Stamp = Guid.NewGuid().ToString(),
                Now = now, Scope = scope
            }, transaction);
        await ReplaceGameAccountsAsync(connection, transaction, id, scope, request.GameAccountIds);
        await AuditAsync(connection, transaction, id, request.Username.Trim(),
            "UserCreated", "Succeeded", null, request.Actor);
        await transaction.CommitAsync();
        return new(id, request.Username.Trim(), request.Role, scope, true,
            request.MustChangePassword, now, null, null);
    }

    public async Task UpdateAsync(ulong id, UpdateAdministrationUserRequest request)
    {
        var scope = ValidateScope(request.AccountScope);
        await using var connection = Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ValidateRoleAsync(connection, request.Role, transaction);
        var target = await connection.QuerySingleAsync<(string Username, string Role)>(
            "SELECT username, role FROM admin_user WHERE id=@id", new { id }, transaction);
        if (target.Role == "Owner" && (request.Role != "Owner" || !request.Enabled)
            && await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM admin_user WHERE role='Owner' AND enabled=1",
                transaction: transaction) <= 1)
            throw new InvalidOperationException("The final enabled Owner cannot be demoted or disabled.");
        await connection.ExecuteAsync("""
            UPDATE admin_user SET role=@Role, account_scope=@Scope, enabled=@Enabled,
              security_stamp=@Stamp WHERE id=@id
            """, new { id, request.Role, Scope = scope, request.Enabled,
                Stamp = Guid.NewGuid().ToString() }, transaction);
        await ReplaceGameAccountsAsync(connection, transaction, id, scope, request.GameAccountIds);
        await AuditAsync(connection, transaction, id, target.Username,
            "UserUpdated", "Succeeded", null, request.Actor);
        await transaction.CommitAsync();
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

    public async Task<IReadOnlyList<AdministrationAuditEntry>> GetAuditAsync(
        string? username = null,
        string? action = null,
        string? outcome = null,
        string? search = null,
        DateTime? fromUtc = null,
        int limit = 200)
    {
        await using var connection = Open();
        return (await connection.QueryAsync<AdministrationAuditEntry>("""
            SELECT id, username, action, outcome, remote_address RemoteAddress,
              detail, occurred_at_utc OccurredAtUtc
            FROM admin_audit_log
            WHERE (@Username IS NULL OR username LIKE CONCAT('%', @Username, '%'))
              AND (@Action IS NULL OR action LIKE CONCAT('%', @Action, '%'))
              AND (@Outcome IS NULL OR outcome=@Outcome)
              AND (@Search IS NULL OR action LIKE CONCAT('%', @Search, '%')
                   OR detail LIKE CONCAT('%', @Search, '%')
                   OR username LIKE CONCAT('%', @Search, '%'))
              AND (@FromUtc IS NULL OR occurred_at_utc >= @FromUtc)
            ORDER BY occurred_at_utc DESC LIMIT @Limit
            """, new {
                Username = NullIfWhiteSpace(username),
                Action = NullIfWhiteSpace(action),
                Outcome = NullIfWhiteSpace(outcome),
                Search = NullIfWhiteSpace(search),
                FromUtc = fromUtc,
                Limit = Math.Clamp(limit, 1, 1000)
            })).AsList();
    }

    public async Task RecordActivityAsync(
        ulong? userId,
        string username,
        string action,
        string outcome,
        string? remoteAddress,
        string? detail)
    {
        await using var connection = Open();
        await AuditAsync(connection, null, userId, username, action, outcome,
            remoteAddress, detail);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<IReadOnlyList<AdministrationPermission>> GetPermissionsAsync()
    {
        await using var connection = Open();
        return (await connection.QueryAsync<AdministrationPermission>("""
            SELECT permission_key `Key`, display_name DisplayName, category, description
            FROM admin_permission ORDER BY category, display_name
            """)).AsList();
    }

    public async Task<IReadOnlyList<uint>> GetUserGameAccountsAsync(ulong id)
    {
        await using var connection = Open();
        return (await connection.QueryAsync<uint>("""
            SELECT game_account_id FROM admin_user_game_account
            WHERE admin_user_id=@id ORDER BY game_account_id
            """, new { id })).AsList();
    }

    public async Task<IReadOnlyList<AdministrationRole>> GetRolesAsync()
    {
        await using var connection = Open();
        var rows = await connection.QueryAsync<RolePermissionRow>("""
            SELECT r.name, r.description, r.is_system IsSystem,
                   rp.permission_key PermissionKey
            FROM admin_role r
            LEFT JOIN admin_role_permission rp ON rp.role_name=r.name
            ORDER BY r.name, rp.permission_key
            """);
        return rows.GroupBy(row => new { row.Name, row.Description, row.IsSystem })
            .Select(group => new AdministrationRole(
                group.Key.Name, group.Key.Description, group.Key.IsSystem,
                group.Where(row => row.PermissionKey is not null)
                    .Select(row => row.PermissionKey!).ToArray()))
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> GetRolePermissionsAsync(string role)
    {
        await using var connection = Open();
        return (await connection.QueryAsync<string>("""
            SELECT permission_key FROM admin_role_permission WHERE role_name=@role
            """, new { role })).AsList();
    }

    public async Task SaveRoleAsync(SaveAdministrationRoleRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length is < 3 or > 32)
            throw new ArgumentException("Role names must contain 3-32 characters.");
        if (name.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Owner role cannot be changed.");
        await using var connection = Open();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var known = (await connection.QueryAsync<string>(
            "SELECT permission_key FROM admin_permission", transaction: transaction))
            .ToHashSet(StringComparer.Ordinal);
        if (request.Permissions.Any(permission => !known.Contains(permission)))
            throw new ArgumentException("The role contains an unknown permission.");
        await connection.ExecuteAsync("""
            INSERT INTO admin_role (name, description, is_system)
            VALUES (@Name, @Description, 0)
            ON DUPLICATE KEY UPDATE description=@Description
            """, new { Name = name, Description = request.Description.Trim() }, transaction);
        await connection.ExecuteAsync(
            "DELETE FROM admin_role_permission WHERE role_name=@Name",
            new { Name = name }, transaction);
        if (request.Permissions.Count > 0)
            await connection.ExecuteAsync("""
                INSERT INTO admin_role_permission (role_name, permission_key)
                VALUES (@Name, @Permission)
                """, request.Permissions.Distinct().Select(
                    permission => new { Name = name, Permission = permission }), transaction);
        await connection.ExecuteAsync("""
            UPDATE admin_user SET security_stamp=@Stamp WHERE role=@Name
            """, new { Name = name, Stamp = Guid.NewGuid().ToString() }, transaction);
        await AuditAsync(connection, transaction, null, name, "RoleSaved",
            "Succeeded", null, request.Actor);
        await transaction.CommitAsync();
    }

    public async Task DeleteRoleAsync(string name, string actor)
    {
        if (name is "Owner" or "Administrator")
            throw new InvalidOperationException("Built-in roles cannot be deleted.");
        await using var connection = Open();
        if (await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM admin_user WHERE role=@name", new { name }) > 0)
            throw new InvalidOperationException("Assign users to another role before deleting it.");
        await connection.ExecuteAsync("DELETE FROM admin_role WHERE name=@name", new { name });
        await WriteAuditAsync(null, name, "RoleDeleted", "Succeeded", actor);
    }

    public async Task<AdministrationUserIdentity?> GetIdentityAsync(ulong id)
    {
        await using var connection = Open();
        return await GetIdentityOrDefaultAsync(connection, id, null);
    }

    private static async Task ReplaceGameAccountsAsync(
        MySqlConnection connection, MySqlTransaction transaction, ulong userId,
        string scope, IReadOnlyList<uint> accountIds)
    {
        await connection.ExecuteAsync(
            "DELETE FROM admin_user_game_account WHERE admin_user_id=@userId",
            new { userId }, transaction);
        // Assigned accounts enforce access. For an All-scope user the same
        // links are harmless preferences used to put their own heroes first.
        if ((scope is "Assigned" or "All") && accountIds.Count > 0)
            await connection.ExecuteAsync("""
                INSERT INTO admin_user_game_account (admin_user_id, game_account_id)
                VALUES (@userId, @accountId)
                """, accountIds.Distinct().Select(accountId => new { userId, accountId }),
                transaction);
    }

    private static async Task<AdministrationUserIdentity> GetIdentityAsync(
        MySqlConnection connection, ulong id, MySqlTransaction? transaction) =>
        await GetIdentityOrDefaultAsync(connection, id, transaction)
        ?? throw new KeyNotFoundException("Administration user not found.");

    private static async Task<AdministrationUserIdentity?> GetIdentityOrDefaultAsync(
        MySqlConnection connection, ulong id, MySqlTransaction? transaction)
    {
        var user = await connection.QuerySingleOrDefaultAsync<IdentityRow>("""
            SELECT id, username, role, account_scope AccountScope,
                   must_change_password MustChangePassword, security_stamp SecurityStamp
            FROM admin_user WHERE id=@id AND enabled=1
            """, new { id }, transaction);
        if (user is null) return null;
        var permissions = (await connection.QueryAsync<string>("""
            SELECT permission_key FROM admin_role_permission WHERE role_name=@Role
            """, new { user.Role }, transaction)).AsList();
        var accounts = (await connection.QueryAsync<uint>("""
            SELECT game_account_id FROM admin_user_game_account WHERE admin_user_id=@id
            """, new { id }, transaction)).AsList();
        return new(user.Id, user.Username, user.Role, user.AccountScope,
            permissions, accounts, user.MustChangePassword, user.SecurityStamp.ToString());
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
    private sealed class IdentityRow
    {
        public ulong Id { get; init; }
        public string Username { get; init; } = "";
        public string Role { get; init; } = "";
        public string AccountScope { get; init; } = "";
        public bool MustChangePassword { get; init; }
        public Guid SecurityStamp { get; init; }
    }
    private sealed class RolePermissionRow
    {
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public bool IsSystem { get; init; }
        public string? PermissionKey { get; init; }
    }
}
