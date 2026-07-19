namespace AzerothCore_UI.Web.Models;

public sealed record CharacterDetails(
    uint Guid,
    uint AccountId,
    string Username,
    string Name,
    byte Level,
    byte Race,
    byte Class,
    bool Online,
    uint Money,
    uint TotalTime,
    ushort Map,
    ushort Zone,
    string LocationName);

public sealed record CharacterQuest(
    uint QuestId,
    string Title,
    byte Status);

public sealed record CompletedCharacterQuest(
    uint QuestId,
    string? Title,
    bool Active);

public static class CharacterDisplayNames
{
    public static string Race(byte race) => race switch
    {
        1 => "Human", 2 => "Orc", 3 => "Dwarf", 4 => "Night Elf",
        5 => "Undead", 6 => "Tauren", 7 => "Gnome", 8 => "Troll",
        10 => "Blood Elf", 11 => "Draenei", _ => $"Unknown ({race})"
    };

    public static string Class(byte characterClass) => characterClass switch
    {
        1 => "Warrior", 2 => "Paladin", 3 => "Hunter", 4 => "Rogue",
        5 => "Priest", 6 => "Death Knight", 7 => "Shaman", 8 => "Mage",
        9 => "Warlock", 11 => "Druid", _ => $"Unknown ({characterClass})"
    };
}
