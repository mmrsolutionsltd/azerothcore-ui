namespace AzerothCore_UI.Web.Models;

public sealed record PlayerActionTarget(
    string Name,
    bool Online,
    bool IsPlayerBot,
    string Username)
{
    public static PlayerActionTarget From(AdministrationPlayer player) =>
        new(player.Name, player.Online, player.IsPlayerBot, player.Username);

    public static PlayerActionTarget From(CharacterDetails character) =>
        new(
            character.Name,
            character.Online,
            character.Username.StartsWith("rndbot", StringComparison.OrdinalIgnoreCase),
            character.Username);
}

public sealed record PlayerActionResult(
    string PlayerName,
    bool Success,
    string Message);
