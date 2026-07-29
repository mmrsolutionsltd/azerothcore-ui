namespace AzerothCore_UI.Web.Models;

public sealed record CharacterPickerItem(
    string Value,
    string Name,
    string Detail,
    bool Online,
    bool IsPlayerBot = false)
{
    public static IReadOnlyList<CharacterPickerItem> FromAdministrationPlayers(
        IEnumerable<AdministrationPlayer> players) =>
        players
            .OrderBy(player => player.PickerOrder)
            .ThenBy(player => player.Name)
            .Select(player => new CharacterPickerItem(
                player.Name,
                player.Name,
                $"Account {player.Username}",
                player.Online,
                player.IsPlayerBot))
            .ToArray();
}
