using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

internal static class DungeonItemStatCatalog
{
    public static IReadOnlyList<DungeonItemStat> Create(
        IEnumerable<(int Type, int Value)> values,
        int holyResistance, int fireResistance, int natureResistance,
        int frostResistance, int shadowResistance, int arcaneResistance)
    {
        var result = values
            .Where(value => value.Value != 0)
            .Select(value => new DungeonItemStat(
                Name(value.Type), value.Value, IsRating(value.Type)))
            .ToList();
        AddResistance(result, "Holy resistance", holyResistance);
        AddResistance(result, "Fire resistance", fireResistance);
        AddResistance(result, "Nature resistance", natureResistance);
        AddResistance(result, "Frost resistance", frostResistance);
        AddResistance(result, "Shadow resistance", shadowResistance);
        AddResistance(result, "Arcane resistance", arcaneResistance);
        return result;
    }

    private static void AddResistance(
        ICollection<DungeonItemStat> result, string name, int value)
    {
        if (value != 0) result.Add(new(name, value));
    }

    private static string Name(int type) => type switch
    {
        0 => "Mana",
        1 => "Health",
        3 => "Agility",
        4 => "Strength",
        5 => "Intellect",
        6 => "Spirit",
        7 => "Stamina",
        12 => "Defense rating",
        13 => "Dodge rating",
        14 => "Parry rating",
        15 => "Block rating",
        16 => "Melee hit rating",
        17 => "Ranged hit rating",
        18 => "Spell hit rating",
        19 => "Melee critical strike rating",
        20 => "Ranged critical strike rating",
        21 => "Spell critical strike rating",
        28 => "Melee haste rating",
        29 => "Ranged haste rating",
        30 => "Spell haste rating",
        31 => "Hit rating",
        32 => "Critical strike rating",
        35 => "Resilience rating",
        36 => "Haste rating",
        37 => "Expertise rating",
        38 => "Attack power",
        39 => "Ranged attack power",
        43 => "Mana per 5 sec",
        44 => "Armor penetration rating",
        45 => "Spell power",
        46 => "Health per 5 sec",
        47 => "Spell penetration",
        48 => "Block value",
        _ => $"Stat {type}"
    };

    private static bool IsRating(int type) =>
        type is >= 12 and <= 21 or >= 28 and <= 37 or 44;
}
