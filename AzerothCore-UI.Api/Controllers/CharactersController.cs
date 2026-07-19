using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AzerothCore_UI.Api.Controllers;

[ApiController]
[Route("api/characters")]
public sealed class CharactersController(
    AzerothCoreConnectionFactory connectionFactory,
    SpellMetadataProvider spellMetadataProvider) : ControllerBase
{
    private static readonly IReadOnlyDictionary<byte, uint> ClassTrainerIds =
        new Dictionary<byte, uint>
        {
            [1] = 1,   // Warrior
            [2] = 3,   // Paladin
            [3] = 7,   // Hunter
            [4] = 9,   // Rogue
            [5] = 11,  // Priest
            [6] = 13,  // Death Knight
            [7] = 14,  // Shaman
            [8] = 16,  // Mage
            [9] = 31,  // Warlock
            [11] = 33  // Druid
        };

    [HttpGet("{guid:long}/training/class")]
    public async Task<ActionResult<IReadOnlyList<MissingClassSpell>>> GetMissingClassSpells(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string characterSql = """
            SELECT
                characters.class AS Class,
                characters.level AS Level
            FROM acore_characters.characters
            WHERE characters.guid = @Guid;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var character = await connection.QuerySingleOrDefaultAsync<CharacterTrainingRow>(
            new CommandDefinition(
                characterSql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken));

        if (character is null)
        {
            return NotFound();
        }

        if (!ClassTrainerIds.TryGetValue(character.Class, out var trainerId))
        {
            return Ok(Array.Empty<MissingClassSpell>());
        }

        const string trainingSql = """
            SELECT
                trainerSpell.SpellId,
                trainerSpell.ReqLevel AS RequiredLevel,
                trainerSpell.MoneyCost AS TrainingCost
            FROM acore_world.trainer_spell AS trainerSpell
            WHERE trainerSpell.TrainerId = @TrainerId
              AND trainerSpell.ReqLevel <= @CharacterLevel
              AND trainerSpell.ReqSkillLine = 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM acore_characters.character_spell AS knownSpell
                  WHERE knownSpell.guid = @Guid
                    AND knownSpell.spell = trainerSpell.SpellId
              )
              AND (
                  trainerSpell.ReqAbility1 = 0
                  OR EXISTS (
                      SELECT 1 FROM acore_characters.character_spell AS ability1
                      WHERE ability1.guid = @Guid AND ability1.spell = trainerSpell.ReqAbility1
                  )
              )
              AND (
                  trainerSpell.ReqAbility2 = 0
                  OR EXISTS (
                      SELECT 1 FROM acore_characters.character_spell AS ability2
                      WHERE ability2.guid = @Guid AND ability2.spell = trainerSpell.ReqAbility2
                  )
              )
              AND (
                  trainerSpell.ReqAbility3 = 0
                  OR EXISTS (
                      SELECT 1 FROM acore_characters.character_spell AS ability3
                      WHERE ability3.guid = @Guid AND ability3.spell = trainerSpell.ReqAbility3
                  )
              )
            ORDER BY trainerSpell.ReqLevel, trainerSpell.SpellId;
            """;

        var spells = await connection.QueryAsync<MissingClassSpellRow>(
            new CommandDefinition(
                trainingSql,
                new
                {
                    Guid = (uint)guid,
                    TrainerId = trainerId,
                    CharacterLevel = character.Level
                },
                cancellationToken: cancellationToken));

        return Ok(spells
            .Select(spell =>
            {
                var metadata = spellMetadataProvider.Find(spell.SpellId);
                return new MissingClassSpell(
                    spell.SpellId,
                    metadata?.Name,
                    metadata?.Rank,
                    spell.RequiredLevel,
                    spell.TrainingCost);
            })
            .ToArray());
    }

    private static readonly IReadOnlyDictionary<ushort, (string Name, string Category)> ProfessionSkills =
        new Dictionary<ushort, (string, string)>
        {
            [129] = ("First Aid", "Secondary"),
            [164] = ("Blacksmithing", "Primary"),
            [165] = ("Leatherworking", "Primary"),
            [171] = ("Alchemy", "Primary"),
            [182] = ("Herbalism", "Primary"),
            [185] = ("Cooking", "Secondary"),
            [186] = ("Mining", "Primary"),
            [197] = ("Tailoring", "Primary"),
            [202] = ("Engineering", "Primary"),
            [333] = ("Enchanting", "Primary"),
            [356] = ("Fishing", "Secondary"),
            [393] = ("Skinning", "Primary"),
            [755] = ("Jewelcrafting", "Primary"),
            [773] = ("Inscription", "Primary")
        };

    [HttpGet("{guid:long}/professions")]
    public async Task<ActionResult<IReadOnlyList<CharacterProfession>>> GetProfessions(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                skills.skill AS SkillId,
                skills.`value` AS Value,
                skills.`max` AS Maximum
            FROM acore_characters.character_skills AS skills
            WHERE skills.guid = @Guid
              AND skills.skill IN @SkillIds
            ORDER BY skills.skill;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CharacterProfessionRow>(
            new CommandDefinition(
                sql,
                new
                {
                    Guid = (uint)guid,
                    SkillIds = ProfessionSkills.Keys.ToArray()
                },
                cancellationToken: cancellationToken));

        var professions = rows
            .Select(row =>
            {
                var profession = ProfessionSkills[row.SkillId];
                return new CharacterProfession(
                    row.SkillId,
                    profession.Name,
                    profession.Category,
                    row.Value,
                    row.Maximum);
            })
            .OrderBy(profession => profession.Category == "Primary" ? 0 : 1)
            .ThenBy(profession => profession.Name)
            .ToArray();

        return Ok(professions);
    }

    [HttpGet("{guid:long}/training/professions")]
    public async Task<ActionResult<IReadOnlyList<MissingProfessionSpell>>> GetMissingProfessionSpells(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                trainerSpell.ReqSkillLine AS SkillId,
                trainerSpell.SpellId,
                trainerSpell.ReqLevel AS RequiredLevel,
                trainerSpell.ReqSkillRank AS RequiredSkillRank,
                skills.`max` AS CurrentMaximum,
                MIN(trainerSpell.MoneyCost) AS TrainingCost
            FROM acore_characters.characters AS characters
            INNER JOIN acore_characters.character_skills AS skills
                ON skills.guid = characters.guid
            INNER JOIN acore_world.trainer_spell AS trainerSpell
                ON trainerSpell.ReqSkillLine = skills.skill
                AND trainerSpell.ReqSkillRank <= skills.`value`
            WHERE characters.guid = @Guid
              AND skills.skill IN @SkillIds
              AND trainerSpell.ReqLevel <= characters.level
              AND NOT EXISTS (
                  SELECT 1
                  FROM acore_characters.character_spell AS knownSpell
                  WHERE knownSpell.guid = characters.guid
                    AND knownSpell.spell = trainerSpell.SpellId
              )
              AND (
                  trainerSpell.ReqAbility1 = 0
                  OR EXISTS (
                      SELECT 1 FROM acore_characters.character_spell AS ability1
                      WHERE ability1.guid = characters.guid
                        AND ability1.spell = trainerSpell.ReqAbility1
                  )
              )
              AND (
                  trainerSpell.ReqAbility2 = 0
                  OR EXISTS (
                      SELECT 1 FROM acore_characters.character_spell AS ability2
                      WHERE ability2.guid = characters.guid
                        AND ability2.spell = trainerSpell.ReqAbility2
                  )
              )
              AND (
                  trainerSpell.ReqAbility3 = 0
                  OR EXISTS (
                      SELECT 1 FROM acore_characters.character_spell AS ability3
                      WHERE ability3.guid = characters.guid
                        AND ability3.spell = trainerSpell.ReqAbility3
                  )
              )
            GROUP BY
                trainerSpell.ReqSkillLine,
                trainerSpell.SpellId,
                trainerSpell.ReqLevel,
                trainerSpell.ReqSkillRank,
                skills.`max`
            ORDER BY trainerSpell.ReqSkillLine, trainerSpell.ReqSkillRank, trainerSpell.SpellId;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<MissingProfessionSpellRow>(
            new CommandDefinition(
                sql,
                new { Guid = (uint)guid, SkillIds = ProfessionSkills.Keys.ToArray() },
                cancellationToken: cancellationToken));

        return Ok(rows
            .Select(row => new { Row = row, Metadata = spellMetadataProvider.Find(row.SpellId) })
            .Where(item => !ProfessionTrainingRules.IsRankAlreadyLearned(
                item.Metadata?.Name,
                item.Row.CurrentMaximum))
            .Select(item => new MissingProfessionSpell(
                item.Row.SkillId,
                ProfessionSkills[item.Row.SkillId].Name,
                item.Row.SpellId,
                item.Metadata?.Name,
                item.Metadata?.Rank,
                item.Row.RequiredLevel,
                item.Row.RequiredSkillRank,
                item.Row.TrainingCost))
            .ToArray());
    }

    [HttpGet("{guid:long}/professions/recipes/vendors")]
    public async Task<ActionResult<IReadOnlyList<MissingVendorRecipe>>> GetMissingVendorRecipes(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string recipeSql = """
            SELECT
                skills.skill AS SkillId,
                item.entry AS ItemId,
                item.name AS ItemName,
                item.spellid_1 AS UseSpellId,
                item.RequiredSkillRank,
                item.BuyPrice,
                item.RequiredReputationFaction AS RequiredFactionId,
                item.RequiredReputationRank,
                NULLIF(faction.Name_Lang_enUS, '') AS FactionName,
                COALESCE(reputation.standing, 0) AS CurrentStanding,
                MAX(CASE WHEN vendor.ExtendedCost <> 0 THEN 1 ELSE 0 END) AS UsesExtendedCost,
                COUNT(DISTINCT vendor.entry) AS VendorCount,
                GROUP_CONCAT(DISTINCT creature.name ORDER BY creature.name SEPARATOR ', ') AS VendorNames
            FROM acore_characters.character_skills AS skills
            INNER JOIN acore_world.item_template AS item
                ON item.RequiredSkill = skills.skill
                AND item.RequiredSkillRank <= skills.`value`
                AND item.spellid_1 <> 0
            INNER JOIN acore_world.npc_vendor AS vendor
                ON vendor.item = item.entry
                AND vendor.entry > 0
            LEFT JOIN acore_world.creature_template AS creature ON creature.entry = vendor.entry
            LEFT JOIN acore_world.faction_dbc AS faction ON faction.ID = item.RequiredReputationFaction
            LEFT JOIN acore_characters.character_reputation AS reputation
                ON reputation.guid = skills.guid AND reputation.faction = item.RequiredReputationFaction
            WHERE skills.guid = @Guid
              AND skills.skill IN @SkillIds
            GROUP BY skills.skill, item.entry, item.name, item.spellid_1,
                item.RequiredSkillRank, item.BuyPrice, item.RequiredReputationFaction,
                item.RequiredReputationRank, faction.Name_Lang_enUS, reputation.standing
            ORDER BY skills.skill, item.RequiredSkillRank, item.name;
            """;

        const string knownSpellSql = """
            SELECT spell
            FROM acore_characters.character_spell
            WHERE guid = @Guid;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<VendorRecipeRow>(
            new CommandDefinition(
                recipeSql,
                new { Guid = (uint)guid, SkillIds = ProfessionSkills.Keys.ToArray() },
                cancellationToken: cancellationToken));
        var knownSpells = (await connection.QueryAsync<uint>(
            new CommandDefinition(
                knownSpellSql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken))).ToHashSet();

        return Ok(rows
            .Select(row => new
            {
                Row = row,
                LearnedSpellId = spellMetadataProvider.Find(row.UseSpellId)?.LearnedSpellId
            })
            .Where(item => item.LearnedSpellId.HasValue
                && !knownSpells.Contains(item.LearnedSpellId.Value))
            .Select(item => new MissingVendorRecipe(
                item.Row.SkillId,
                ProfessionSkills[item.Row.SkillId].Name,
                item.Row.ItemId,
                item.Row.ItemName,
                item.LearnedSpellId!.Value,
                spellMetadataProvider.Find(item.LearnedSpellId.Value)?.Name,
                item.Row.RequiredSkillRank,
                item.Row.BuyPrice,
                item.Row.UsesExtendedCost != 0,
                item.Row.VendorCount,
                item.Row.VendorNames ?? "Unknown vendor",
                item.Row.RequiredFactionId,
                item.Row.FactionName,
                item.Row.RequiredReputationRank,
                item.Row.CurrentStanding,
                item.Row.RequiredFactionId == 0
                    || GetReputationRank(item.Row.CurrentStanding) >= item.Row.RequiredReputationRank))
            .ToArray());
    }

    [HttpGet("{guid:long}/professions/recipes/quests")]
    public async Task<ActionResult<IReadOnlyList<MissingQuestRecipe>>> GetMissingQuestRecipes(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string recipeSql = """
            SELECT DISTINCT
                skills.skill AS SkillId,
                quest.ID AS QuestId,
                quest.LogTitle AS QuestTitle,
                quest.MinLevel AS MinimumLevel,
                rewards.RewardKind,
                item.entry AS ItemId,
                item.name AS ItemName,
                item.spellid_1 AS UseSpellId,
                item.RequiredSkillRank
            FROM acore_characters.characters AS characters
            INNER JOIN acore_characters.character_skills AS skills ON skills.guid = characters.guid
            INNER JOIN (
                SELECT ID, RewardItem1 AS ItemId, 'Guaranteed' AS RewardKind FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardItem2, 'Guaranteed' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardItem3, 'Guaranteed' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardItem4, 'Guaranteed' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardChoiceItemID1, 'Choice' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardChoiceItemID2, 'Choice' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardChoiceItemID3, 'Choice' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardChoiceItemID4, 'Choice' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardChoiceItemID5, 'Choice' FROM acore_world.quest_template
                UNION ALL SELECT ID, RewardChoiceItemID6, 'Choice' FROM acore_world.quest_template
            ) AS rewards ON rewards.ItemId <> 0
            INNER JOIN acore_world.quest_template AS quest ON quest.ID = rewards.ID
            INNER JOIN acore_world.item_template AS item
                ON item.entry = rewards.ItemId
                AND item.RequiredSkill = skills.skill
                AND item.RequiredSkillRank <= skills.`value`
                AND item.spellid_1 <> 0
            WHERE characters.guid = @Guid
              AND skills.skill IN @SkillIds
              AND quest.MinLevel <= characters.level
              AND NOT EXISTS (
                  SELECT 1
                  FROM acore_characters.character_queststatus_rewarded AS completedQuest
                  WHERE completedQuest.guid = characters.guid
                    AND completedQuest.quest = quest.ID
              )
            ORDER BY skills.skill, item.RequiredSkillRank, quest.LogTitle;
            """;

        const string knownSpellSql = """
            SELECT spell FROM acore_characters.character_spell WHERE guid = @Guid;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<QuestRecipeRow>(
            new CommandDefinition(
                recipeSql,
                new { Guid = (uint)guid, SkillIds = ProfessionSkills.Keys.ToArray() },
                cancellationToken: cancellationToken));
        var knownSpells = (await connection.QueryAsync<uint>(
            new CommandDefinition(
                knownSpellSql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken))).ToHashSet();

        return Ok(rows
            .Select(row => new
            {
                Row = row,
                LearnedSpellId = spellMetadataProvider.Find(row.UseSpellId)?.LearnedSpellId
            })
            .Where(item => item.LearnedSpellId.HasValue
                && !knownSpells.Contains(item.LearnedSpellId.Value))
            .Select(item => new MissingQuestRecipe(
                item.Row.SkillId,
                ProfessionSkills[item.Row.SkillId].Name,
                item.Row.QuestId,
                item.Row.QuestTitle,
                item.Row.MinimumLevel,
                item.Row.RewardKind,
                item.Row.ItemId,
                item.Row.ItemName,
                item.LearnedSpellId!.Value,
                spellMetadataProvider.Find(item.LearnedSpellId.Value)?.Name,
                item.Row.RequiredSkillRank))
            .ToArray());
    }

    [HttpGet("{guid:long}/professions/recipes/loot")]
    public async Task<ActionResult<IReadOnlyList<MissingLootRecipe>>> GetMissingLootRecipes(
        long guid, CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue) return BadRequest("The character GUID is outside the supported range.");

        const string sql = """
            SELECT skills.skill AS SkillId, item.entry AS ItemId, item.name AS ItemName,
                item.spellid_1 AS UseSpellId, item.RequiredSkillRank, sources.SourceType,
                GROUP_CONCAT(DISTINCT sources.SourceName ORDER BY sources.SourceName SEPARATOR ', ') AS SourceNames,
                MAX(sources.DropChance) AS DropChance
            FROM acore_characters.character_skills AS skills
            INNER JOIN acore_world.item_template AS item ON item.RequiredSkill = skills.skill
                AND item.RequiredSkillRank <= skills.`value` AND item.spellid_1 <> 0
            INNER JOIN (
                SELECT loot.Item AS ItemId, 'Creature' AS SourceType, creature.name AS SourceName, loot.Chance AS DropChance
                FROM acore_world.creature_loot_template AS loot
                INNER JOIN acore_world.creature_template AS creature ON creature.lootid = loot.Entry
                WHERE loot.Item <> 0
                UNION ALL
                SELECT referenceLoot.Item, 'Creature (referenced loot)', creature.name, referenceLoot.Chance
                FROM acore_world.reference_loot_template AS referenceLoot
                INNER JOIN acore_world.creature_loot_template AS loot ON loot.Reference = referenceLoot.Entry
                INNER JOIN acore_world.creature_template AS creature ON creature.lootid = loot.Entry
                WHERE referenceLoot.Item <> 0
                UNION ALL
                SELECT loot.Item, 'Game object', gameObject.name, loot.Chance
                FROM acore_world.gameobject_loot_template AS loot
                INNER JOIN acore_world.gameobject_template AS gameObject ON gameObject.data1 = loot.Entry
                    AND gameObject.type IN (3, 25)
                WHERE loot.Item <> 0
                UNION ALL
                SELECT referenceLoot.Item, 'Game object (referenced loot)', gameObject.name, referenceLoot.Chance
                FROM acore_world.reference_loot_template AS referenceLoot
                INNER JOIN acore_world.gameobject_loot_template AS loot ON loot.Reference = referenceLoot.Entry
                INNER JOIN acore_world.gameobject_template AS gameObject ON gameObject.data1 = loot.Entry
                    AND gameObject.type IN (3, 25)
                WHERE referenceLoot.Item <> 0
            ) AS sources ON sources.ItemId = item.entry
            WHERE skills.guid = @Guid AND skills.skill IN @SkillIds
            GROUP BY skills.skill, item.entry, item.name, item.spellid_1,
                item.RequiredSkillRank, sources.SourceType
            ORDER BY skills.skill, item.RequiredSkillRank, item.name, sources.SourceType;
            """;

        var (rows, knownSpells) = await QueryRecipeRows<LootRecipeRow>(sql, (uint)guid, cancellationToken);
        return Ok(rows.Select(row => MapLearnedRecipe(row, knownSpells))
            .Where(item => item is not null).Select(item => new MissingLootRecipe(
                item!.Row.SkillId, ProfessionSkills[item.Row.SkillId].Name, item.Row.ItemId,
                item.Row.ItemName, item.LearnedSpellId, spellMetadataProvider.Find(item.LearnedSpellId)?.Name,
                item.Row.RequiredSkillRank, item.Row.SourceType, item.Row.SourceNames, item.Row.DropChance)).ToArray());
    }

    [HttpGet("{guid:long}/professions/recipes/unclassified")]
    public async Task<ActionResult<IReadOnlyList<MissingUnclassifiedRecipe>>> GetUnclassifiedRecipes(
        long guid, CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue) return BadRequest("The character GUID is outside the supported range.");

        const string sql = """
            SELECT skills.skill AS SkillId, item.entry AS ItemId, item.name AS ItemName,
                item.spellid_1 AS UseSpellId, item.RequiredSkillRank
            FROM acore_characters.character_skills AS skills
            INNER JOIN acore_world.item_template AS item ON item.RequiredSkill = skills.skill
                AND item.RequiredSkillRank <= skills.`value` AND item.spellid_1 <> 0
            WHERE skills.guid = @Guid AND skills.skill IN @SkillIds
              AND NOT EXISTS (SELECT 1 FROM acore_world.npc_vendor AS vendor WHERE vendor.item = item.entry)
              AND NOT EXISTS (SELECT 1 FROM acore_world.quest_template AS quest WHERE item.entry IN
                  (quest.RewardItem1, quest.RewardItem2, quest.RewardItem3, quest.RewardItem4,
                   quest.RewardChoiceItemID1, quest.RewardChoiceItemID2, quest.RewardChoiceItemID3,
                   quest.RewardChoiceItemID4, quest.RewardChoiceItemID5, quest.RewardChoiceItemID6))
              AND NOT EXISTS (SELECT 1 FROM acore_world.creature_loot_template WHERE Item = item.entry)
              AND NOT EXISTS (SELECT 1 FROM acore_world.gameobject_loot_template WHERE Item = item.entry)
              AND NOT EXISTS (SELECT 1 FROM acore_world.reference_loot_template WHERE Item = item.entry)
            ORDER BY skills.skill, item.RequiredSkillRank, item.name;
            """;

        var (rows, knownSpells) = await QueryRecipeRows<RecipeItemRow>(sql, (uint)guid, cancellationToken);
        return Ok(rows.Select(row => MapLearnedRecipe(row, knownSpells))
            .Where(item => item is not null).Select(item => new MissingUnclassifiedRecipe(
                item!.Row.SkillId, ProfessionSkills[item.Row.SkillId].Name, item.Row.ItemId,
                item.Row.ItemName, item.LearnedSpellId, spellMetadataProvider.Find(item.LearnedSpellId)?.Name,
                item.Row.RequiredSkillRank)).ToArray());
    }

    private async Task<(IReadOnlyList<T> Rows, HashSet<uint> KnownSpells)> QueryRecipeRows<T>(
        string sql, uint guid, CancellationToken cancellationToken) where T : RecipeItemRow
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<T>(new CommandDefinition(sql,
            new { Guid = guid, SkillIds = ProfessionSkills.Keys.ToArray() },
            cancellationToken: cancellationToken))).ToArray();
        var known = (await connection.QueryAsync<uint>(new CommandDefinition(
            "SELECT spell FROM acore_characters.character_spell WHERE guid = @Guid;",
            new { Guid = guid }, cancellationToken: cancellationToken))).ToHashSet();
        return (rows, known);
    }

    private LearnedRecipe<T>? MapLearnedRecipe<T>(T row, HashSet<uint> knownSpells) where T : RecipeItemRow
    {
        var learnedSpellId = spellMetadataProvider.Find(row.UseSpellId)?.LearnedSpellId;
        return learnedSpellId.HasValue && !knownSpells.Contains(learnedSpellId.Value)
            ? new LearnedRecipe<T>(row, learnedSpellId.Value) : null;
    }

    [HttpGet("{guid:long}/inventory/bags")]
    public async Task<ActionResult<IReadOnlyList<BagItem>>> GetBagItems(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                inventory.bag AS BagGuid,
                CASE
                    WHEN inventory.bag = 0 THEN 'Backpack'
                    ELSE COALESCE(bagTemplate.name, CONCAT('Unknown bag (', inventory.bag, ')'))
                END AS BagName,
                inventory.slot AS Slot,
                instance.guid AS ItemGuid,
                instance.itemEntry AS ItemEntry,
                template.name AS Name,
                template.Quality AS Quality,
                template.ItemLevel AS ItemLevel,
                instance.count AS Count
            FROM acore_characters.character_inventory AS inventory
            INNER JOIN acore_characters.item_instance AS instance ON instance.guid = inventory.item
            LEFT JOIN acore_world.item_template AS template ON template.entry = instance.itemEntry
            LEFT JOIN acore_characters.character_inventory AS equippedBag
                ON equippedBag.guid = inventory.guid
                AND equippedBag.item = inventory.bag
                AND equippedBag.bag = 0
                AND equippedBag.slot BETWEEN 19 AND 22
            LEFT JOIN acore_characters.item_instance AS bagInstance ON bagInstance.guid = inventory.bag
            LEFT JOIN acore_world.item_template AS bagTemplate ON bagTemplate.entry = bagInstance.itemEntry
            WHERE inventory.guid = @Guid
              AND (
                  (inventory.bag = 0 AND inventory.slot BETWEEN 23 AND 38)
                  OR equippedBag.item IS NOT NULL
              )
            ORDER BY
                CASE WHEN inventory.bag = 0 THEN 0 ELSE equippedBag.slot + 1 END,
                inventory.slot;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var items = await connection.QueryAsync<BagItemRow>(
            new CommandDefinition(
                sql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken));

        return Ok(items
            .Select(item => new BagItem(
                item.BagGuid,
                item.BagName,
                item.Slot,
                item.ItemGuid,
                item.ItemEntry,
                item.Name,
                item.Quality,
                item.ItemLevel,
                item.Count))
            .ToArray());
    }

    [HttpGet("{guid:long}/inventory/equipped")]
    public async Task<ActionResult<IReadOnlyList<EquippedItem>>> GetEquippedItems(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                inventory.slot AS Slot,
                instance.guid AS ItemGuid,
                instance.itemEntry AS ItemEntry,
                template.name AS Name,
                template.Quality AS Quality,
                template.ItemLevel AS ItemLevel,
                instance.count AS Count,
                instance.durability AS Durability,
                template.MaxDurability AS MaxDurability
            FROM acore_characters.character_inventory AS inventory
            INNER JOIN acore_characters.item_instance AS instance ON instance.guid = inventory.item
            LEFT JOIN acore_world.item_template AS template ON template.entry = instance.itemEntry
            WHERE inventory.guid = @Guid
              AND inventory.bag = 0
              AND inventory.slot BETWEEN 0 AND 18
            ORDER BY inventory.slot;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var items = await connection.QueryAsync<EquippedItemRow>(
            new CommandDefinition(
                sql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken));

        return Ok(items
            .Select(item => new EquippedItem(
                item.Slot,
                item.ItemGuid,
                item.ItemEntry,
                item.Name,
                item.Quality,
                item.ItemLevel,
                item.Count,
                item.Durability,
                item.MaxDurability))
            .ToArray());
    }

    [HttpGet("{guid:long}/quests/completed")]
    public async Task<ActionResult<IReadOnlyList<CompletedCharacterQuest>>> GetCompletedCharacterQuests(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                rewarded.quest AS QuestId,
                template.LogTitle AS Title,
                rewarded.active AS ActiveValue
            FROM acore_characters.character_queststatus_rewarded AS rewarded
            LEFT JOIN acore_world.quest_template AS template ON template.ID = rewarded.quest
            WHERE rewarded.guid = @Guid
            ORDER BY template.LogTitle, rewarded.quest;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CompletedCharacterQuestRow>(
            new CommandDefinition(
                sql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken));

        return Ok(rows
            .Select(row => new CompletedCharacterQuest(
                row.QuestId,
                row.Title,
                row.ActiveValue != 0))
            .ToArray());
    }

    [HttpGet("{guid:long}/quests")]
    public async Task<ActionResult<IReadOnlyList<CharacterQuest>>> GetCharacterQuests(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                qs.quest AS QuestId,
                qt.LogTitle AS Title,
                qs.status AS Status
            FROM acore_characters.character_queststatus AS qs
            INNER JOIN acore_world.quest_template AS qt ON qt.ID = qs.quest
            WHERE qs.guid = @Guid
            ORDER BY qt.LogTitle, qs.quest;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var quests = await connection.QueryAsync<CharacterQuest>(
            new CommandDefinition(
                sql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken));

        return Ok(quests.ToArray());
    }

    [HttpGet("{guid:long}")]
    public async Task<ActionResult<CharacterDetails>> GetCharacter(
        long guid,
        CancellationToken cancellationToken)
    {
        if (guid is < 0 or > uint.MaxValue)
        {
            return BadRequest("The character GUID is outside the supported range.");
        }

        const string sql = """
            SELECT
                c.guid AS Guid,
                a.id AS AccountId,
                a.username AS Username,
                c.name AS Name,
                c.level AS Level,
                c.race AS Race,
                c.class AS Class,
                c.online AS OnlineValue,
                c.money AS Money,
                c.totaltime AS TotalTime,
                c.map AS Map,
                c.zone AS Zone,
                NULLIF(area.AreaName_Lang_enUS, '') AS DatabaseLocationName
            FROM acore_characters.characters AS c
            INNER JOIN acore_auth.account AS a ON a.id = c.account
            LEFT JOIN acore_world.areatable_dbc AS area ON area.ID = c.zone
            WHERE c.guid = @Guid;
            """;

        await using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<CharacterDetailsRow>(
            new CommandDefinition(
                sql,
                new { Guid = (uint)guid },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return NotFound();
        }

        return Ok(new CharacterDetails(
            row.Guid,
            row.AccountId,
            row.Username,
            row.Name,
            row.Level,
            row.Race,
            row.Class,
            row.OnlineValue != 0,
            row.Money,
            row.TotalTime,
            row.Map,
            row.Zone,
            AreaNameResolver.Resolve(row.Zone, row.DatabaseLocationName)));
    }

    private sealed class CharacterDetailsRow
    {
        public uint Guid { get; init; }
        public uint AccountId { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public byte Level { get; init; }
        public byte Race { get; init; }
        public byte Class { get; init; }
        public byte OnlineValue { get; init; }
        public uint Money { get; init; }
        public uint TotalTime { get; init; }
        public ushort Map { get; init; }
        public ushort Zone { get; init; }
        public string? DatabaseLocationName { get; init; }
    }

    private sealed class CompletedCharacterQuestRow
    {
        public uint QuestId { get; init; }
        public string? Title { get; init; }
        public byte ActiveValue { get; init; }
    }

    private sealed class EquippedItemRow
    {
        public byte Slot { get; init; }
        public uint ItemGuid { get; init; }
        public uint ItemEntry { get; init; }
        public string? Name { get; init; }
        public byte Quality { get; init; }
        public ushort ItemLevel { get; init; }
        public uint Count { get; init; }
        public ushort Durability { get; init; }
        public ushort MaxDurability { get; init; }
    }

    private sealed class BagItemRow
    {
        public uint BagGuid { get; init; }
        public string BagName { get; init; } = string.Empty;
        public byte Slot { get; init; }
        public uint ItemGuid { get; init; }
        public uint ItemEntry { get; init; }
        public string? Name { get; init; }
        public byte Quality { get; init; }
        public ushort ItemLevel { get; init; }
        public uint Count { get; init; }
    }

    private sealed class CharacterProfessionRow
    {
        public ushort SkillId { get; init; }
        public ushort Value { get; init; }
        public ushort Maximum { get; init; }
    }

    private sealed class CharacterTrainingRow
    {
        public byte Class { get; init; }
        public byte Level { get; init; }
    }

    private sealed class MissingProfessionSpellRow
    {
        public ushort SkillId { get; init; }
        public uint SpellId { get; init; }
        public byte RequiredLevel { get; init; }
        public ushort RequiredSkillRank { get; init; }
        public ushort CurrentMaximum { get; init; }
        public uint TrainingCost { get; init; }
    }

    private sealed class VendorRecipeRow
    {
        public ushort SkillId { get; init; }
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint UseSpellId { get; init; }
        public ushort RequiredSkillRank { get; init; }
        public uint BuyPrice { get; init; }
        public byte UsesExtendedCost { get; init; }
        public int VendorCount { get; init; }
        public string? VendorNames { get; init; }
        public ushort RequiredFactionId { get; init; }
        public string? FactionName { get; init; }
        public byte RequiredReputationRank { get; init; }
        public int CurrentStanding { get; init; }
    }

    private static byte GetReputationRank(int standing) => standing switch
    {
        < -6000 => 0, < -3000 => 1, < 0 => 2, < 3000 => 3,
        < 9000 => 4, < 21000 => 5, < 42000 => 6, _ => 7
    };

    private sealed class QuestRecipeRow
    {
        public ushort SkillId { get; init; }
        public uint QuestId { get; init; }
        public string QuestTitle { get; init; } = string.Empty;
        public byte MinimumLevel { get; init; }
        public string RewardKind { get; init; } = string.Empty;
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint UseSpellId { get; init; }
        public ushort RequiredSkillRank { get; init; }
    }

    private class RecipeItemRow
    {
        public ushort SkillId { get; init; }
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint UseSpellId { get; init; }
        public ushort RequiredSkillRank { get; init; }
    }

    private sealed class LootRecipeRow : RecipeItemRow
    {
        public string SourceType { get; init; } = string.Empty;
        public string SourceNames { get; init; } = string.Empty;
        public float? DropChance { get; init; }
    }

    private sealed record LearnedRecipe<T>(T Row, uint LearnedSpellId) where T : RecipeItemRow;

    private sealed class MissingClassSpellRow
    {
        public uint SpellId { get; init; }
        public byte RequiredLevel { get; init; }
        public uint TrainingCost { get; init; }
    }
}
