namespace AzerothCore_UI.Api.Services;

internal sealed record CharacterServiceCommand(string Command, string Message, bool RequiresOnlineCharacter = false);

internal static class CharacterServiceCommandBuilder
{
    internal static CharacterServiceCommand Build(string player, string? service, int? level) =>
        service?.ToLowerInvariant() switch
        {
            "rename" => new($"character rename {player}", "Rename required at next login."),
            "customize" => new($"character customize {player}", "Appearance customization enabled for next login."),
            "race" => new($"character changerace {player}", "Race change enabled for next login."),
            "faction" => new($"character changefaction {player}", "Faction change enabled for next login."),
            "talents" => new($"reset talents {player}", "Character and pet talents reset."),
            "spells" => new($"reset spells {player}", "Character spells reset.", true),
            "revive" => new($"revive {player}", "Character revived."),
            "unstuck" => new($"unstuck {player} inn", "Character moved to their home inn."),
            "level" when level is >= 1 and <= 80 =>
                new($"character level {player} {level}", $"Character level changed to {level}."),
            "level" => throw new ArgumentOutOfRangeException(nameof(level),
                "Character level must be between 1 and 80."),
            _ => throw new ArgumentException("Unknown character service.", nameof(service))
        };
}
