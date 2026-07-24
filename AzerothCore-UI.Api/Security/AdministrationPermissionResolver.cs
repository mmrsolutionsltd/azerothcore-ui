namespace AzerothCore_UI.Api.Security;

public static class AdministrationPermissionResolver
{
    public static string? RequiredPermission(string method, string path)
    {
        if (path.StartsWith("/api/administration-users/audit")) return "security.audit";
        if (path.StartsWith("/api/administration-users/roles")
            || path.StartsWith("/api/administration-users/permissions")) return "security.roles";
        if (path.StartsWith("/api/administration-users")) return "security.users";
        if (path.StartsWith("/api/database-backups")) return "server.backups";
        if (path.StartsWith("/api/diagnostics")) return "server.diagnostics";
        if (path.StartsWith("/api/auction-house")) return "world.auction-house";
        if (path.StartsWith("/api/quest-helper")) return "adventures.quests";
        if (path.StartsWith("/api/starter-presets")) return "players.services";
        if (path.StartsWith("/api/training")) return "adventures.training";
        if (path.StartsWith("/api/accounts")) return "players.accounts";
        if (path.StartsWith("/api/characters")) return "players.characters";
        if (path.StartsWith("/api/server-administration/settings")) return "server.settings";
        if (path.EndsWith("/start") || path.EndsWith("/stop") || path.EndsWith("/restart")
            || path.EndsWith("/status")) return "server.control";
        if (path.Contains("/creatures")) return "world.creatures";
        if (path.Contains("/parties") || path.Contains("/dungeons")) return "adventures.dungeons";
        if (path.Contains("/weapon-training")) return "adventures.training";
        if (path.Contains("/collectibles")) return "players.collectibles";
        if (path.Contains("/characters/service")) return "players.services";
        if (path.StartsWith("/api/server-administration")) return "players.actions";
        return null;
    }
}
