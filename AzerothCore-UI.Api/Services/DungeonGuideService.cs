using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Services;

public sealed class DungeonGuideService(AzerothCoreConnectionFactory connections)
{
    public sealed record Character(int CharacterClass, int CharacterLevel);

    public async Task<DungeonGuide> GetAsync(
        DungeonDestination dungeon, IReadOnlyList<Character> characters,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        var endEntry = await connection.QuerySingleOrDefaultAsync<uint?>(
            new CommandDefinition("""
                SELECT MAX(entry) FROM acore_world.instance_encounters
                WHERE lastEncounterDungeon=@DungeonId
                """, new { dungeon.DungeonId }, cancellationToken: cancellationToken));
        IReadOnlyList<EncounterRow> encounters = endEntry is not null
            ? (await connection.QueryAsync<EncounterRow>(new CommandDefinition("""
                SELECT entry EncounterEntry, creditEntry CreatureId, comment Name
                FROM acore_world.instance_encounters
                WHERE creditType=0 AND entry BETWEEN
                  (SELECT COALESCE(MAX(entry), 0) + 1
                   FROM acore_world.instance_encounters
                   WHERE entry < @EndEntry AND lastEncounterDungeon<>0)
                  AND @EndEntry
                ORDER BY entry
                """, new { EndEntry = endEntry.Value },
                cancellationToken: cancellationToken))).AsList()
            : (await connection.QueryAsync<EncounterRow>(new CommandDefinition("""
                SELECT 0 EncounterEntry, template.entry CreatureId, template.name Name
                FROM acore_world.creature creature
                JOIN acore_world.creature_template template
                  ON template.entry=creature.id
                WHERE creature.map=@MapId AND template.rank=3 AND template.lootid<>0
                GROUP BY template.entry, template.name, template.minlevel
                ORDER BY template.minlevel, template.name
                """, new { dungeon.MapId },
                cancellationToken: cancellationToken))).AsList();
        var bossIds = encounters.Select(encounter => encounter.CreatureId)
            .Distinct().ToArray();
        IReadOnlyList<LootRow> lootRows = bossIds.Length == 0
            ? []
            : (await connection.QueryAsync<LootRow>(new CommandDefinition("""
                SELECT template.entry BossCreatureId, item.entry ItemId,
                       item.name Name, item.Quality Quality,
                       item.ItemLevel ItemLevel, item.RequiredLevel RequiredLevel,
                       item.class ItemClass, item.subclass ItemSubclass,
                       item.InventoryType InventoryType,
                       CAST(item.AllowableClass AS SIGNED) AllowableClass,
                       item.armor Armor, item.dmg_min1 MinimumDamage,
                       item.dmg_max1 MaximumDamage, item.delay DelayMilliseconds,
                       item.holy_res HolyResistance, item.fire_res FireResistance,
                       item.nature_res NatureResistance, item.frost_res FrostResistance,
                       item.shadow_res ShadowResistance, item.arcane_res ArcaneResistance,
                       item.stat_type1 StatType1, item.stat_value1 StatValue1,
                       item.stat_type2 StatType2, item.stat_value2 StatValue2,
                       item.stat_type3 StatType3, item.stat_value3 StatValue3,
                       item.stat_type4 StatType4, item.stat_value4 StatValue4,
                       item.stat_type5 StatType5, item.stat_value5 StatValue5,
                       item.stat_type6 StatType6, item.stat_value6 StatValue6,
                       item.stat_type7 StatType7, item.stat_value7 StatValue7,
                       item.stat_type8 StatType8, item.stat_value8 StatValue8,
                       item.stat_type9 StatType9, item.stat_value9 StatValue9,
                       item.stat_type10 StatType10, item.stat_value10 StatValue10,
                       ABS(loot.Chance) DropChance,
                       loot.QuestRequired<>0 QuestRequired
                FROM acore_world.creature_template template
                JOIN acore_world.creature_loot_template loot
                  ON loot.Entry=template.lootid
                JOIN acore_world.item_template item ON item.entry=loot.Item
                WHERE template.entry IN @BossIds AND loot.Item<>0
                  AND (item.Quality>=2 OR loot.QuestRequired<>0)
                UNION ALL
                SELECT template.entry, item.entry, item.name, item.Quality,
                       item.ItemLevel, item.RequiredLevel, item.class,
                       item.subclass, item.InventoryType,
                       CAST(item.AllowableClass AS SIGNED),
                       item.armor, item.dmg_min1, item.dmg_max1, item.delay,
                       item.holy_res, item.fire_res, item.nature_res,
                       item.frost_res, item.shadow_res, item.arcane_res,
                       item.stat_type1, item.stat_value1,
                       item.stat_type2, item.stat_value2,
                       item.stat_type3, item.stat_value3,
                       item.stat_type4, item.stat_value4,
                       item.stat_type5, item.stat_value5,
                       item.stat_type6, item.stat_value6,
                       item.stat_type7, item.stat_value7,
                       item.stat_type8, item.stat_value8,
                       item.stat_type9, item.stat_value9,
                       item.stat_type10, item.stat_value10,
                       ABS(referenceLoot.Chance),
                       referenceLoot.QuestRequired<>0
                FROM acore_world.creature_template template
                JOIN acore_world.creature_loot_template parentLoot
                  ON parentLoot.Entry=template.lootid
                JOIN acore_world.reference_loot_template referenceLoot
                  ON referenceLoot.Entry=parentLoot.Reference
                JOIN acore_world.item_template item ON item.entry=referenceLoot.Item
                WHERE template.entry IN @BossIds
                  AND parentLoot.Reference<>0 AND referenceLoot.Item<>0
                  AND (item.Quality>=2 OR referenceLoot.QuestRequired<>0)
                """, new { BossIds = bossIds },
                cancellationToken: cancellationToken))).AsList();
        var catalog = DungeonGuideCatalog.Find(dungeon.Name);
        var bosses = encounters.Select((encounter, index) =>
        {
            var loot = lootRows.Where(row => row.BossCreatureId == encounter.CreatureId)
                .GroupBy(row => row.ItemId)
                .Select(group => group.OrderByDescending(row => row.DropChance).First())
                .Select(row => new DungeonLootItem
                {
                    ItemId = row.ItemId, Name = row.Name, Quality = row.Quality,
                    ItemLevel = row.ItemLevel, RequiredLevel = row.RequiredLevel,
                    ItemClass = row.ItemClass, ItemSubclass = row.ItemSubclass,
                    InventoryType = row.InventoryType, AllowableClass = row.AllowableClass,
                    DropChance = row.DropChance, QuestRequired = row.QuestRequired,
                    SuggestedForParty = IsSuggested(row, characters),
                    Armor = row.Armor, MinimumDamage = row.MinimumDamage,
                    MaximumDamage = row.MaximumDamage,
                    DelayMilliseconds = row.DelayMilliseconds,
                    Stats = DungeonItemStatCatalog.Create(
                        row.StatValues(),
                        row.HolyResistance, row.FireResistance,
                        row.NatureResistance, row.FrostResistance,
                        row.ShadowResistance, row.ArcaneResistance)
                })
                .OrderByDescending(item => item.SuggestedForParty)
                .ThenByDescending(item => item.Quality)
                .ThenBy(item => item.Name).ToArray();
            return new DungeonBossGuide(index + 1, encounter.CreatureId,
                encounter.Name, DungeonGuideCatalog.Tactics(catalog, encounter.Name), loot);
        }).ToArray();
        return new(dungeon.DungeonId, dungeon.Name, catalog.Overview, catalog.Route,
            catalog.Notes, bosses);
    }

    private static bool IsSuggested(LootRow item, IReadOnlyList<Character> characters) =>
        characters.Count == 0 || characters.Any(character =>
            (item.AllowableClass is -1 or 0
                || (item.AllowableClass & (1L << (character.CharacterClass - 1))) != 0)
            && item.RequiredLevel <= character.CharacterLevel + 5);

    private sealed class EncounterRow
    {
        public uint EncounterEntry { get; init; }
        public uint CreatureId { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class LootRow
    {
        public uint BossCreatureId { get; init; }
        public uint ItemId { get; init; }
        public string Name { get; init; } = "";
        public int Quality { get; init; }
        public int ItemLevel { get; init; }
        public int RequiredLevel { get; init; }
        public int ItemClass { get; init; }
        public int ItemSubclass { get; init; }
        public int InventoryType { get; init; }
        public long AllowableClass { get; init; }
        public double DropChance { get; init; }
        public bool QuestRequired { get; init; }
        public int Armor { get; init; }
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
}
