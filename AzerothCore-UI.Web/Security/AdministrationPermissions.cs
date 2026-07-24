namespace AzerothCore_UI.Web.Security;

public static class AdministrationPermissions
{
    public const string ClaimType = "permission";
    public static readonly string[] All =
    [
        "players.accounts", "players.characters", "players.actions",
        "players.services", "players.collectibles", "adventures.quests",
        "adventures.dungeons", "adventures.training", "world.auction-house",
        "world.creatures", "server.control", "server.settings",
        "server.diagnostics", "server.backups", "security.users",
        "security.roles", "security.audit"
    ];
}
