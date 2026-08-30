using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

internal static class CraftingUpgradeRules
{
    public static readonly IReadOnlyDictionary<int, string> SlotNames =
        new Dictionary<int, string>
        {
            [0] = "Head", [1] = "Neck", [2] = "Shoulders", [3] = "Shirt",
            [4] = "Chest", [5] = "Waist", [6] = "Legs", [7] = "Feet",
            [8] = "Wrists", [9] = "Hands", [10] = "Finger 1",
            [11] = "Finger 2", [12] = "Trinket 1", [13] = "Trinket 2",
            [14] = "Back", [15] = "Main hand", [16] = "Off hand",
            [17] = "Ranged / relic", [18] = "Tabard"
        };

    public static int? ChooseSlot(
        CraftingItemData item,
        IReadOnlyDictionary<int, CraftingItemData> equipped)
    {
        var slots = CandidateSlots(item.InventoryType);
        if (slots.Count == 0)
            return null;
        return slots.FirstOrDefault(slot => !equipped.ContainsKey(slot), -1) is var empty
            && empty >= 0
            ? empty
            : slots.OrderBy(slot => equipped.GetValueOrDefault(slot)?.ItemLevel ?? 0)
                .First();
    }

    public static bool IsUsable(
        CraftingItemData item, int characterClass, int race, int level)
    {
        if (item.RequiredLevel > level || CandidateSlots(item.InventoryType).Count == 0)
            return false;
        var classMask = 1L << (characterClass - 1);
        var raceMask = 1L << (race - 1);
        return (item.AllowableClass is -1 or 0
                || (item.AllowableClass & classMask) != 0)
            && (item.AllowableRace is -1 or 0
                || (item.AllowableRace & raceMask) != 0)
            && (item.ItemClass != 2
                || WeaponSubclasses(characterClass).Contains(item.ItemSubclass))
            && (item.ItemClass != 4
                || ArmorSubclasses(characterClass, level).Contains(item.ItemSubclass));
    }

    public static CraftingGearItem ToGearItem(CraftingItemData item) => new(
        item.ItemId, item.Name, item.Quality, item.ItemLevel,
        item.RequiredLevel, item.InventoryType, item.ItemClass,
        item.ItemSubclass, Stats(item));

    public static IReadOnlyList<CraftingStatDelta> Deltas(
        CraftingItemData? equipped, CraftingItemData candidate)
    {
        var current = equipped is null
            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            : Stats(equipped).ToDictionary(stat => stat.Name, stat => stat.Value,
                StringComparer.OrdinalIgnoreCase);
        var proposed = Stats(candidate).ToDictionary(
            stat => stat.Name, stat => stat.Value,
            StringComparer.OrdinalIgnoreCase);
        return current.Keys.Union(proposed.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name => new CraftingStatDelta(
                name, current.GetValueOrDefault(name), proposed.GetValueOrDefault(name),
                proposed.GetValueOrDefault(name) - current.GetValueOrDefault(name)))
            .Where(delta => Math.Abs(delta.Difference) > 0.001)
            .OrderByDescending(delta => delta.Difference > 0)
            .ThenBy(delta => delta.Difference < 0)
            .ThenBy(delta => delta.Name)
            .ToArray();
    }

    public static bool IsPotentialUpgrade(
        CraftingItemData? equipped, CraftingItemData candidate) =>
        equipped is null || candidate.ItemLevel > equipped.ItemLevel;

    private static IReadOnlyList<CraftingGearStat> Stats(CraftingItemData item)
    {
        var stats = DungeonItemStatCatalog.Create(
                item.StatValues(), item.HolyResistance, item.FireResistance,
                item.NatureResistance, item.FrostResistance,
                item.ShadowResistance, item.ArcaneResistance)
            .Select(stat => new CraftingGearStat(stat.Name, stat.Value))
            .ToList();
        if (item.Armor != 0)
            stats.Insert(0, new("Armor", item.Armor));
        if (item.Block != 0)
            stats.Add(new("Block", item.Block));
        if (item.DelayMilliseconds > 0 && item.MaximumDamage > 0)
        {
            var dps = (item.MinimumDamage + item.MaximumDamage) / 2.0
                / (item.DelayMilliseconds / 1000.0);
            stats.Insert(0, new("Weapon DPS", Math.Round(dps, 1)));
        }
        return stats;
    }

    private static IReadOnlyList<int> CandidateSlots(int inventoryType) =>
        inventoryType switch
        {
            1 => [0], 2 => [1], 3 => [2], 4 => [3], 5 or 20 => [4],
            6 => [5], 7 => [6], 8 => [7], 9 => [8], 10 => [9],
            11 => [10, 11], 12 => [12, 13], 13 or 17 or 21 => [15],
            14 or 22 or 23 => [16], 15 or 25 or 26 or 28 => [17],
            16 => [14], 19 => [18], _ => []
        };

    private static int[] WeaponSubclasses(int characterClass) =>
        characterClass switch
        {
            1 => [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 13, 15, 16, 18],
            2 => [0, 1, 4, 5, 6, 7, 8],
            3 => [0, 1, 2, 3, 6, 7, 8, 10, 13, 15, 18],
            4 => [0, 2, 3, 4, 7, 13, 15, 16, 18],
            5 => [4, 10, 15, 19],
            6 => [0, 1, 4, 5, 6, 7, 8],
            7 => [0, 1, 4, 5, 10, 13, 15],
            8 or 9 => [7, 10, 15, 19],
            11 => [4, 5, 6, 10, 13, 15],
            _ => [0]
        };

    private static int[] ArmorSubclasses(int characterClass, int level) =>
        characterClass switch
        {
            1 => level >= 40 ? [0, 1, 2, 3, 4, 6] : [0, 1, 2, 3, 6],
            2 => level >= 40 ? [0, 1, 2, 3, 4, 6, 7] : [0, 1, 2, 3, 6, 7],
            3 => level >= 40 ? [0, 1, 2, 3] : [0, 1, 2],
            4 => [0, 1, 2],
            5 or 8 or 9 => [0, 1],
            6 => [0, 1, 2, 3, 4, 10],
            7 => level >= 40 ? [0, 1, 2, 3, 6, 9] : [0, 1, 2, 6, 9],
            11 => [0, 1, 2, 8],
            _ => [0]
        };
}

internal class CraftingItemData
{
    public uint ItemId { get; init; }
    public string Name { get; init; } = "";
    public int Quality { get; init; }
    public int ItemLevel { get; init; }
    public int RequiredLevel { get; init; }
    public int ItemClass { get; init; }
    public int ItemSubclass { get; init; }
    public int InventoryType { get; init; }
    public long AllowableClass { get; init; }
    public long AllowableRace { get; init; }
    public int Armor { get; init; }
    public int Block { get; init; }
    public double MinimumDamage { get; init; }
    public double MaximumDamage { get; init; }
    public int DelayMilliseconds { get; init; }
    public int HolyResistance { get; init; }
    public int FireResistance { get; init; }
    public int NatureResistance { get; init; }
    public int FrostResistance { get; init; }
    public int ShadowResistance { get; init; }
    public int ArcaneResistance { get; init; }
    public int StatType1 { get; init; }
    public int StatValue1 { get; init; }
    public int StatType2 { get; init; }
    public int StatValue2 { get; init; }
    public int StatType3 { get; init; }
    public int StatValue3 { get; init; }
    public int StatType4 { get; init; }
    public int StatValue4 { get; init; }
    public int StatType5 { get; init; }
    public int StatValue5 { get; init; }
    public int StatType6 { get; init; }
    public int StatValue6 { get; init; }
    public int StatType7 { get; init; }
    public int StatValue7 { get; init; }
    public int StatType8 { get; init; }
    public int StatValue8 { get; init; }
    public int StatType9 { get; init; }
    public int StatValue9 { get; init; }
    public int StatType10 { get; init; }
    public int StatValue10 { get; init; }

    public (int Type, int Value)[] StatValues() =>
    [
        (StatType1, StatValue1), (StatType2, StatValue2),
        (StatType3, StatValue3), (StatType4, StatValue4),
        (StatType5, StatValue5), (StatType6, StatValue6),
        (StatType7, StatValue7), (StatType8, StatValue8),
        (StatType9, StatValue9), (StatType10, StatValue10)
    ];
}
