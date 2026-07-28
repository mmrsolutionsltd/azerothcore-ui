using System.Text.Json;
using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Security;

public sealed class AdministrationRequestAuthorizer(
    AdministrationAccountStore users,
    AzerothCoreConnectionFactory connections)
{
    private static readonly string[] AnonymousAdministrationPaths =
    [
        "/api/administration-users/state",
        "/api/administration-users/bootstrap",
        "/api/administration-users/authenticate",
        "/api/administration-users/validate-session",
        "/api/administration-users/change-password"
    ];

    public async Task<AuthorizationDecision> AuthorizeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (AnonymousAdministrationPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            return AuthorizationDecision.Allow;
        if (!ulong.TryParse(context.Request.Headers["X-AzerothCore-Actor-Id"], out var userId))
            return new(false, "An authenticated website user is required.");
        var identity = await users.GetIdentityAsync(userId);
        if (identity is null)
            return new(false, "The website session is no longer valid.");
        context.Items[typeof(AdministrationUserIdentity)] = identity;
        var permission = AdministrationPermissionResolver.RequiredPermission(
            context.Request.Method, path);
        if (permission is not null && !identity.Permissions.Contains(permission))
            return new(false, $"Permission '{permission}' is required.");
        if (identity.AccountScope == "All")
            return AuthorizationDecision.Allow;
        var targets = await ReadTargetsAsync(context);
        if (targets.AccountIds.Any(id => !identity.GameAccountIds.Contains(id)))
            return new(false, "The selected game account is outside your assigned scope.");
        if (targets.CharacterGuids.Count > 0 || targets.PlayerNames.Count > 0)
        {
            await using var connection = connections.CreateConnection();
            var accountIds = new List<uint>();
            if (targets.CharacterGuids.Count > 0)
                accountIds.AddRange(await connection.QueryAsync<uint>("""
                    SELECT account FROM acore_characters.characters WHERE guid IN @Guids
                    """, new { Guids = targets.CharacterGuids }));
            if (targets.PlayerNames.Count > 0)
                accountIds.AddRange(await connection.QueryAsync<uint>("""
                    SELECT account FROM acore_characters.characters WHERE name IN @Names
                    """, new { Names = targets.PlayerNames }));
            if (accountIds.Any(id => !identity.GameAccountIds.Contains(id)))
                return new(false, "One or more selected characters are outside your assigned scope.");
        }
        return AuthorizationDecision.Allow;
    }

    private static async Task<Targets> ReadTargetsAsync(HttpContext context)
    {
        var result = new Targets();
        if (uint.TryParse(context.Request.RouteValues["guid"]?.ToString(), out var guid))
            result.CharacterGuids.Add(guid);
        if (uint.TryParse(context.Request.RouteValues["accountId"]?.ToString(), out var accountId))
            result.AccountIds.Add(accountId);
        foreach (var key in new[] { "playerName", "leaderName" })
            if (context.Request.RouteValues[key]?.ToString() is { Length: > 0 } name)
                result.PlayerNames.Add(name);
        if (context.Request.ContentLength is null or 0
            || context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return result;
        context.Request.EnableBuffering();
        using var document = await JsonDocument.ParseAsync(context.Request.Body);
        context.Request.Body.Position = 0;
        Collect(document.RootElement, result);
        return result;
    }

    private static void Collect(JsonElement element, Targets targets, string? propertyName = null)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                Collect(property.Value, targets, property.Name);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                Collect(child, targets, propertyName);
        else if (element.ValueKind == JsonValueKind.String
            && propertyName is "playerName" or "anchorPlayerName" or "leaderName"
                or "companionName")
            targets.PlayerNames.Add(element.GetString()!);
        else if (element.ValueKind == JsonValueKind.String && propertyName == "playerNames")
            targets.PlayerNames.Add(element.GetString()!);
        else if (element.ValueKind == JsonValueKind.Number
            && propertyName is "characterGuid" or "characterGuids" or "guid"
            && element.TryGetUInt32(out var guid))
            targets.CharacterGuids.Add(guid);
        else if (element.ValueKind == JsonValueKind.Number
            && propertyName == "accountId" && element.TryGetUInt32(out var accountId))
            targets.AccountIds.Add(accountId);
    }

    private sealed class Targets
    {
        public HashSet<uint> AccountIds { get; } = [];
        public HashSet<uint> CharacterGuids { get; } = [];
        public HashSet<string> PlayerNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record AuthorizationDecision(bool Allowed, string? Message)
{
    public static AuthorizationDecision Allow { get; } = new(true, null);
}
