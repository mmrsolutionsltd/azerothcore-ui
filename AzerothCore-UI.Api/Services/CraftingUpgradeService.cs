using AzerothCore_UI.Api.Data;
using AzerothCore_UI.Api.Models;
using Dapper;

namespace AzerothCore_UI.Api.Services;

public sealed class CraftingUpgradeService(
    AzerothCoreConnectionFactory connections,
    CraftingRecipeCatalog recipeCatalog,
    SpellMetadataProvider spellMetadata)
{
    private static readonly ProfessionTier[] ProfessionTiers =
    [
        new("Journeyman", 50, 150, 10),
        new("Expert", 125, 225, 20),
        new("Artisan", 200, 300, 35),
        new("Master", 275, 375, 50),
        new("Grand Master", 350, 450, 65)
    ];

    public async Task<CraftingUpgradePlan?> GetAsync(
        uint targetGuid, bool allAccounts, IReadOnlyCollection<uint> allowedAccounts,
        int maximumSkillGap, int futureLevelHorizon, bool includeSidegrades,
        CancellationToken cancellationToken)
    {
        maximumSkillGap = Math.Clamp(maximumSkillGap, 0, 450);
        futureLevelHorizon = Math.Clamp(futureLevelHorizon, 0, 80);
        await using var connection = connections.CreateConnection();
        var characterRows = (await connection.QueryAsync<CharacterProfessionRow>(
            new CommandDefinition("""
                SELECT c.guid Guid, c.account AccountId, a.username Username,
                       c.name Name, c.level Level, c.race Race,
                       c.class CharacterClass, c.online<>0 Online,
                       skills.skill SkillId, skills.`value` CurrentSkill,
                       skills.`max` MaximumSkill
                FROM acore_characters.characters c
                JOIN acore_auth.account a ON a.id=c.account
                LEFT JOIN acore_characters.character_skills skills
                  ON skills.guid=c.guid AND skills.skill IN @ProfessionSkills
                WHERE a.username NOT LIKE 'rndbot%'
                  AND a.username<>'AHBOT'
                  AND (@AllAccounts OR c.account IN @AllowedAccounts)
                ORDER BY c.name, skills.skill
                """, new
            {
                ProfessionSkills = ProfessionCatalog.All.Keys.ToArray(),
                AllAccounts = allAccounts,
                AllowedAccounts = allowedAccounts.ToArray()
            }, cancellationToken: cancellationToken))).AsList();

        var target = characterRows.FirstOrDefault(row => row.Guid == targetGuid);
        if (target is null)
            return null;

        var characters = characterRows
            .GroupBy(row => row.Guid)
            .Select(group => group.First())
            .ToArray();
        var professionRows = characterRows.Where(row => row.SkillId.HasValue)
            .ToArray();
        var professionSummaries = professionRows.Select(row =>
        {
            var profession = ProfessionCatalog.All.GetValueOrDefault(row.SkillId!.Value);
            return new CraftingProfessionSummary(
                row.Guid, row.Name, row.Username, row.SkillId.Value,
                profession?.Name ?? $"Skill {row.SkillId}",
                row.CurrentSkill, row.MaximumSkill);
        }).OrderBy(row => row.ProfessionName).ThenBy(row => row.CharacterName)
          .ToArray();

        var crafterGuids = professionRows.Select(row => row.Guid).Distinct().ToArray();
        var knownSpells = crafterGuids.Length == 0
            ? []
            : (await connection.QueryAsync<KnownSpellRow>(new CommandDefinition("""
                SELECT guid Guid, spell SpellId
                FROM acore_characters.character_spell
                WHERE guid IN @Guids
                """, new { Guids = crafterGuids },
                cancellationToken: cancellationToken))).AsList();
        var knownByCharacter = knownSpells
            .GroupBy(row => row.Guid)
            .ToDictionary(group => group.Key,
                group => group.Select(row => row.SpellId).ToHashSet());

        var professionSkillIds = professionRows.Select(row => row.SkillId!.Value)
            .Distinct().ToHashSet();
        var recipes = recipeCatalog.Recipes
            .Where(recipe => professionSkillIds.Contains(recipe.SkillId))
            .ToArray();
        var outputIds = recipes.Select(recipe => recipe.OutputItemId)
            .Distinct().ToArray();
        var outputItems = outputIds.Length == 0
            ? new Dictionary<uint, CraftingItemData>()
            : (await connection.QueryAsync<CraftingItemData>(new CommandDefinition(
                $"SELECT {ItemProjection("item")} FROM acore_world.item_template item " +
                "WHERE item.entry IN @ItemIds AND item.InventoryType<>0",
                new { ItemIds = outputIds }, cancellationToken: cancellationToken)))
                .ToDictionary(item => item.ItemId);

        var equippedRows = (await connection.QueryAsync<EquippedCraftingItemRow>(
            new CommandDefinition(
                $"SELECT inventory.slot EquipmentSlot, {ItemProjection("item")} " +
                "FROM acore_characters.character_inventory inventory " +
                "JOIN acore_characters.item_instance instance ON instance.guid=inventory.item " +
                "JOIN acore_world.item_template item ON item.entry=instance.itemEntry " +
                "WHERE inventory.guid=@Guid AND inventory.bag=0 AND inventory.slot<19",
                new { Guid = targetGuid }, cancellationToken: cancellationToken)))
            .ToDictionary(item => item.EquipmentSlot,
                item => (CraftingItemData)item);

        var scopedGuids = characters.Select(character => character.Guid).ToArray();
        var ownedItems = scopedGuids.Length == 0
            ? []
            : (await connection.QueryAsync<OwnedCraftingItemRow>(new CommandDefinition(
                $"""
                SELECT owner.guid SourceCharacterGuid, owner.name SourceCharacterName,
                       account.username SourceUsername,
                       CASE
                         WHEN (inventory.bag=0 AND inventory.slot BETWEEN 39 AND 74)
                           OR (containerSlot.bag=0 AND containerSlot.slot BETWEEN 67 AND 74)
                         THEN 'Bank'
                         ELSE 'Bags'
                       END SourceLocation,
                       {ItemProjection("item")}
                FROM acore_characters.character_inventory inventory
                JOIN acore_characters.characters owner ON owner.guid=inventory.guid
                JOIN acore_auth.account account ON account.id=owner.account
                JOIN acore_characters.item_instance instance ON instance.guid=inventory.item
                JOIN acore_world.item_template item ON item.entry=instance.itemEntry
                LEFT JOIN acore_characters.character_inventory containerSlot
                  ON containerSlot.guid=inventory.guid AND containerSlot.item=inventory.bag
                WHERE inventory.guid IN @Guids
                  AND NOT (inventory.bag=0 AND inventory.slot<23)
                  AND item.InventoryType<>0
                UNION ALL
                SELECT owner.guid, owner.name, account.username, 'Mail',
                       {ItemProjection("item")}
                FROM acore_characters.mail_items mailItem
                JOIN acore_characters.characters owner ON owner.guid=mailItem.receiver
                JOIN acore_auth.account account ON account.id=owner.account
                JOIN acore_characters.item_instance instance ON instance.guid=mailItem.item_guid
                JOIN acore_world.item_template item ON item.entry=instance.itemEntry
                WHERE mailItem.receiver IN @Guids AND item.InventoryType<>0
                """, new { Guids = scopedGuids },
                cancellationToken: cancellationToken))).AsList();

        var recipeSources = await GetRecipeSourcesAsync(
            connection, recipes, cancellationToken);
        var reagentIds = recipes.SelectMany(recipe => recipe.Reagents)
            .Select(reagent => reagent.ItemId).Distinct().ToArray();
        var reagentNames = reagentIds.Length == 0
            ? new Dictionary<uint, string>()
            : (await connection.QueryAsync<ItemNameRow>(new CommandDefinition("""
                SELECT entry ItemId, name Name FROM acore_world.item_template
                WHERE entry IN @ItemIds
                """, new { ItemIds = reagentIds },
                cancellationToken: cancellationToken)))
                .ToDictionary(row => row.ItemId, row => row.Name);
        var materialCounts = await GetMaterialCountsAsync(
            connection, scopedGuids, reagentIds, cancellationToken);

        var recommendations = new Dictionary<int, List<CraftingUpgradeRecommendation>>();
        foreach (var owned in ownedItems)
        {
            AddRecommendation(owned, "Owned", owned.SourceCharacterGuid,
                owned.SourceCharacterName, owned.SourceUsername,
                owned.SourceLocation, null, null, null, null, null, 0,
                null, "Already owned", owned.SourceLocation, [], [],
                target, equippedRows, futureLevelHorizon, includeSidegrades,
                recommendations);
        }

        var recipesBySkill = recipes.GroupBy(recipe => recipe.SkillId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var profession in professionRows)
        {
            var skillId = profession.SkillId!.Value;
            if (!recipesBySkill.TryGetValue(skillId, out var professionRecipes))
                continue;
            var known = knownByCharacter.GetValueOrDefault(profession.Guid) ?? [];
            var professionName = ProfessionCatalog.All.GetValueOrDefault(skillId)?.Name
                ?? $"Skill {skillId}";
            foreach (var recipe in professionRecipes)
            {
                if (!outputItems.TryGetValue(recipe.OutputItemId, out var item))
                    continue;
                var source = recipeSources.GetValueOrDefault(
                    (recipe.SkillId, recipe.SpellId));
                var requiredSkill = Math.Max(recipe.RequiredSkill,
                    source?.RequiredSkill ?? 0);
                var knowsRecipe = known.Contains(recipe.SpellId);
                var gap = Math.Max(0, requiredSkill - profession.CurrentSkill);
                if (!knowsRecipe && gap > maximumSkillGap)
                    continue;
                var availability = knowsRecipe && gap == 0 ? "CraftNow"
                    : gap == 0 ? "LearnNext" : "Progression";
                var materials = recipe.Reagents.Select(reagent =>
                    new CraftingMaterialRequirement(
                        reagent.ItemId,
                        reagentNames.GetValueOrDefault(reagent.ItemId,
                            $"Item {reagent.ItemId}"),
                        reagent.Quantity,
                        materialCounts.GetValueOrDefault(
                            (profession.Guid, reagent.ItemId)),
                        materialCounts.GetValueOrDefault((0u, reagent.ItemId))))
                    .ToArray();
                AddRecommendation(item, availability, profession.Guid,
                    profession.Name, profession.Username, "Profession",
                    skillId, professionName, profession.CurrentSkill,
                    profession.MaximumSkill, requiredSkill, gap, recipe.SpellId,
                    spellMetadata.Find(recipe.SpellId)?.Name ?? item.Name,
                    knowsRecipe ? "Recipe already known"
                        : source?.Description ?? "Discovery or special recipe source",
                    BuildProgressionSteps(professionName, profession.Level,
                        profession.CurrentSkill, profession.MaximumSkill,
                        requiredSkill, knowsRecipe,
                        source?.Description ?? "Find or discover the recipe",
                        materials),
                    materials, target, equippedRows, futureLevelHorizon,
                    includeSidegrades, recommendations);
            }
        }

        var slots = CraftingUpgradeRules.SlotNames.Select(slot =>
        {
            equippedRows.TryGetValue(slot.Key, out var equipped);
            var values = recommendations.GetValueOrDefault(slot.Key) ?? [];
            return new CraftingGearSlot(
                slot.Key, slot.Value,
                equipped is null ? null : CraftingUpgradeRules.ToGearItem(equipped),
                values
                    .DistinctBy(value => (
                        value.Availability, value.Item.ItemId,
                        value.SourceCharacterGuid, value.SourceLocation))
                    .OrderBy(value => AvailabilityOrder(value.Availability))
                    .ThenByDescending(value => value.PotentialUpgrade)
                    .ThenByDescending(value => value.Item.ItemLevel)
                    .ThenBy(value => value.SkillGap)
                    .ThenBy(value => value.Item.Name)
                    .Take(40).ToArray());
        }).ToArray();
        var allRecommendations = slots.SelectMany(slot => slot.Recommendations)
            .ToArray();
        return new CraftingUpgradePlan(
            new CraftingTargetCharacter(
                target.Guid, target.Name, target.Username, target.Level,
                target.Race, target.CharacterClass, target.Online),
            professionSummaries, slots,
            allRecommendations.Count(value => value.Availability == "Owned"),
            allRecommendations.Count(value => value.Availability == "CraftNow"),
            allRecommendations.Count(value => value.Availability == "LearnNext"),
            allRecommendations.Count(value => value.Availability == "Progression"),
            recipeCatalog.DataSource);
    }

    private void AddRecommendation(
        CraftingItemData item, string availability,
        uint? sourceCharacterGuid, string sourceCharacterName,
        string sourceUsername, string sourceLocation,
        ushort? professionSkillId, string? professionName,
        int? currentSkill, int? maximumSkill, int? requiredSkill, int skillGap,
        uint? craftSpellId, string recipeName, string recipeSource,
        IReadOnlyList<CraftingProgressionStep> progressionSteps,
        IReadOnlyList<CraftingMaterialRequirement> materials,
        CharacterProfessionRow target,
        IReadOnlyDictionary<int, CraftingItemData> equipped,
        int futureLevelHorizon, bool includeSidegrades,
        IDictionary<int, List<CraftingUpgradeRecommendation>> recommendations)
    {
        if (!CraftingUpgradeRules.IsUsable(
                item, target.CharacterClass, target.Race,
                target.Level + futureLevelHorizon))
            return;
        var slot = CraftingUpgradeRules.ChooseSlot(item, equipped);
        if (!slot.HasValue)
            return;
        equipped.TryGetValue(slot.Value, out var current);
        var potentialUpgrade = CraftingUpgradeRules.IsPotentialUpgrade(current, item);
        if (!potentialUpgrade && !includeSidegrades)
            return;
        var recommendation = new CraftingUpgradeRecommendation(
            availability, CraftingUpgradeRules.ToGearItem(item),
            CraftingUpgradeRules.IsUsable(
                item, target.CharacterClass, target.Race, target.Level),
            potentialUpgrade, sourceCharacterGuid, sourceCharacterName,
            sourceUsername, sourceLocation, professionSkillId, professionName,
            currentSkill, maximumSkill, requiredSkill, skillGap, craftSpellId,
            recipeName, recipeSource, progressionSteps, materials,
            CraftingUpgradeRules.Deltas(current, item));
        if (!recommendations.TryGetValue(slot.Value, out var slotValues))
            recommendations[slot.Value] = slotValues = [];
        slotValues.Add(recommendation);
    }

    internal static IReadOnlyList<CraftingProgressionStep> BuildProgressionSteps(
        string professionName, int characterLevel, int currentSkill,
        int maximumSkill, int requiredSkill, bool knowsRecipe,
        string recipeSource, IReadOnlyList<CraftingMaterialRequirement> materials)
    {
        var steps = new List<CraftingProgressionStep>();
        var simulatedSkill = currentSkill;
        var simulatedMaximum = maximumSkill;
        var order = 1;
        foreach (var tier in ProfessionTiers.Where(tier =>
                     requiredSkill > simulatedMaximum && tier.MaximumSkill > simulatedMaximum))
        {
            if (simulatedSkill < tier.TrainingSkill)
            {
                steps.Add(new(order++, "Skill",
                    $"Raise {professionName} to {tier.TrainingSkill}",
                    $"Practice orange or yellow recipes; current skill is {simulatedSkill}.",
                    false));
                simulatedSkill = tier.TrainingSkill;
            }
            steps.Add(new(order++, "Training", $"Train {tier.Name} {professionName}",
                characterLevel >= tier.CharacterLevel
                    ? $"Visit a {professionName} trainer to raise the cap to {tier.MaximumSkill}."
                    : $"Reach character level {tier.CharacterLevel}, then visit a {professionName} trainer to raise the cap to {tier.MaximumSkill}.",
                false));
            simulatedMaximum = tier.MaximumSkill;
        }

        if (simulatedSkill < requiredSkill)
            steps.Add(new(order++, "Skill", $"Reach {requiredSkill} {professionName}",
                $"Gain {requiredSkill - simulatedSkill} more profession skill.", false));

        steps.Add(knowsRecipe
            ? new(order++, "Recipe", "Recipe known",
                "This artisan already knows the recipe.", true)
            : new(order++, "Recipe", "Obtain the recipe", recipeSource, false));

        var missing = materials.Where(material =>
            material.ScopedQuantity < material.RequiredQuantity).ToArray();
        steps.Add(new(order++, "Materials",
            missing.Length == 0 ? "Materials ready" : "Gather the missing materials",
            missing.Length == 0
                ? "The scoped characters currently hold enough of every reagent."
                : string.Join(", ", missing.Select(material =>
                    $"{material.RequiredQuantity - material.ScopedQuantity} × {material.Name}")),
            missing.Length == 0));
        steps.Add(new(order, "Craft", "Create the upgrade",
            $"Craft it on the {professionName} character and deliver it to the target character.",
            false));
        return steps;
    }

    private async Task<Dictionary<(ushort SkillId, uint SpellId), RecipeSource>>
        GetRecipeSourcesAsync(
            System.Data.Common.DbConnection connection,
            IReadOnlyCollection<CraftingRecipeDefinition> recipes,
            CancellationToken cancellationToken)
    {
        if (recipes.Count == 0)
            return [];
        var skills = recipes.Select(recipe => recipe.SkillId).Distinct().ToArray();
        var spells = recipes.Select(recipe => recipe.SpellId).Distinct().ToArray();
        var sources = new Dictionary<(ushort, uint), RecipeSource>();
        var trainers = await connection.QueryAsync<TrainerRecipeRow>(
            new CommandDefinition("""
                SELECT ReqSkillLine SkillId, SpellId,
                       MIN(ReqSkillRank) RequiredSkill
                FROM acore_world.trainer_spell
                WHERE ReqSkillLine IN @Skills AND SpellId IN @Spells
                GROUP BY ReqSkillLine, SpellId
                """, new { Skills = skills, Spells = spells },
                cancellationToken: cancellationToken));
        foreach (var trainer in trainers)
            sources[(trainer.SkillId, trainer.SpellId)] = new RecipeSource(
                trainer.RequiredSkill, "Learn from a profession trainer");

        var allRecipeItems = (await connection.QueryAsync<RecipeItemSourceRow>(
            new CommandDefinition("""
                SELECT item.entry RecipeItemId, item.name RecipeItemName,
                       item.RequiredSkill SkillId,
                       item.RequiredSkillRank RequiredSkill,
                       item.spellid_1 UseSpellId
                FROM acore_world.item_template item
                WHERE item.RequiredSkill IN @Skills AND item.spellid_1<>0
                """, new { Skills = skills }, cancellationToken: cancellationToken)))
            .AsList();
        var recipeItems = allRecipeItems.Select(item => new
            {
                Item = item,
                LearnedSpellId = spellMetadata.Find(item.UseSpellId)?.LearnedSpellId
            })
            .Where(value => value.LearnedSpellId.HasValue
                && spells.Contains(value.LearnedSpellId.Value))
            .ToArray();
        var recipeItemIds = recipeItems.Select(value => value.Item.RecipeItemId)
            .Distinct().ToArray();
        if (recipeItemIds.Length == 0)
            return sources;

        var vendors = (await connection.QueryAsync<RecipeSourceNamesRow>(
            new CommandDefinition("""
                SELECT vendor.item RecipeItemId,
                       GROUP_CONCAT(DISTINCT creature.name
                         ORDER BY creature.name SEPARATOR ', ') SourceNames
                FROM acore_world.npc_vendor vendor
                LEFT JOIN acore_world.creature_template creature
                  ON creature.entry=vendor.entry
                WHERE vendor.item IN @ItemIds
                GROUP BY vendor.item
                """, new { ItemIds = recipeItemIds },
                cancellationToken: cancellationToken)))
            .ToDictionary(row => row.RecipeItemId, row => row.SourceNames);
        var quests = (await connection.QueryAsync<RecipeSourceNamesRow>(
            new CommandDefinition("""
                SELECT rewards.ItemId RecipeItemId,
                       GROUP_CONCAT(DISTINCT quest.LogTitle
                         ORDER BY quest.LogTitle SEPARATOR ', ') SourceNames
                FROM (
                    SELECT ID, RewardItem1 ItemId FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardItem2 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardItem3 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardItem4 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardChoiceItemID1 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardChoiceItemID2 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardChoiceItemID3 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardChoiceItemID4 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardChoiceItemID5 FROM acore_world.quest_template
                    UNION ALL SELECT ID, RewardChoiceItemID6 FROM acore_world.quest_template
                ) rewards
                JOIN acore_world.quest_template quest ON quest.ID=rewards.ID
                WHERE rewards.ItemId IN @ItemIds
                GROUP BY rewards.ItemId
                """, new { ItemIds = recipeItemIds },
                cancellationToken: cancellationToken)))
            .ToDictionary(row => row.RecipeItemId, row => row.SourceNames);
        var lootItems = (await connection.QueryAsync<uint>(
            new CommandDefinition("""
                SELECT Item FROM acore_world.creature_loot_template WHERE Item IN @ItemIds
                UNION SELECT Item FROM acore_world.gameobject_loot_template WHERE Item IN @ItemIds
                UNION SELECT Item FROM acore_world.reference_loot_template WHERE Item IN @ItemIds
                """, new { ItemIds = recipeItemIds },
                cancellationToken: cancellationToken))).ToHashSet();

        foreach (var recipeItem in recipeItems)
        {
            var item = recipeItem.Item;
            var vendorNames = vendors.GetValueOrDefault(item.RecipeItemId);
            var questNames = quests.GetValueOrDefault(item.RecipeItemId);
            var description = !string.IsNullOrWhiteSpace(vendorNames)
                ? $"Buy {item.RecipeItemName} from {vendorNames}"
                : !string.IsNullOrWhiteSpace(questNames)
                    ? $"Earn {item.RecipeItemName} from {questNames}"
                    : lootItems.Contains(item.RecipeItemId)
                        ? $"Find {item.RecipeItemName} as loot"
                        : $"Find or discover {item.RecipeItemName}";
            sources[(item.SkillId, recipeItem.LearnedSpellId!.Value)] =
                new RecipeSource(item.RequiredSkill, description);
        }
        return sources;
    }

    private static async Task<Dictionary<(uint Guid, uint ItemId), int>>
        GetMaterialCountsAsync(
            System.Data.Common.DbConnection connection, uint[] guids,
            uint[] itemIds, CancellationToken cancellationToken)
    {
        if (guids.Length == 0 || itemIds.Length == 0)
            return [];
        var rows = await connection.QueryAsync<MaterialCountRow>(
            new CommandDefinition("""
                SELECT inventory.guid Guid, instance.itemEntry ItemId,
                       SUM(instance.count) Quantity
                FROM acore_characters.character_inventory inventory
                JOIN acore_characters.item_instance instance
                  ON instance.guid=inventory.item
                WHERE inventory.guid IN @Guids AND instance.itemEntry IN @ItemIds
                GROUP BY inventory.guid, instance.itemEntry
                UNION ALL
                SELECT mailItem.receiver, instance.itemEntry, SUM(instance.count)
                FROM acore_characters.mail_items mailItem
                JOIN acore_characters.item_instance instance
                  ON instance.guid=mailItem.item_guid
                WHERE mailItem.receiver IN @Guids AND instance.itemEntry IN @ItemIds
                GROUP BY mailItem.receiver, instance.itemEntry
                """, new { Guids = guids, ItemIds = itemIds },
                cancellationToken: cancellationToken));
        var result = rows.GroupBy(row => (row.Guid, row.ItemId))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Quantity));
        foreach (var group in rows.GroupBy(row => row.ItemId))
            result[(0, group.Key)] = group.Sum(row => row.Quantity);
        return result;
    }

    private static int AvailabilityOrder(string value) => value switch
    {
        "Owned" => 0, "CraftNow" => 1, "LearnNext" => 2,
        "Progression" => 3, _ => 4
    };

    private static string ItemProjection(string alias) => $"""
        {alias}.entry ItemId, {alias}.name Name, {alias}.Quality Quality,
        {alias}.ItemLevel ItemLevel, {alias}.RequiredLevel RequiredLevel,
        {alias}.class ItemClass, {alias}.subclass ItemSubclass,
        {alias}.InventoryType InventoryType,
        CAST({alias}.AllowableClass AS SIGNED) AllowableClass,
        CAST({alias}.AllowableRace AS SIGNED) AllowableRace,
        {alias}.armor Armor, {alias}.block Block,
        {alias}.dmg_min1 MinimumDamage, {alias}.dmg_max1 MaximumDamage,
        {alias}.delay DelayMilliseconds,
        {alias}.holy_res HolyResistance, {alias}.fire_res FireResistance,
        {alias}.nature_res NatureResistance, {alias}.frost_res FrostResistance,
        {alias}.shadow_res ShadowResistance, {alias}.arcane_res ArcaneResistance,
        {alias}.stat_type1 StatType1, {alias}.stat_value1 StatValue1,
        {alias}.stat_type2 StatType2, {alias}.stat_value2 StatValue2,
        {alias}.stat_type3 StatType3, {alias}.stat_value3 StatValue3,
        {alias}.stat_type4 StatType4, {alias}.stat_value4 StatValue4,
        {alias}.stat_type5 StatType5, {alias}.stat_value5 StatValue5,
        {alias}.stat_type6 StatType6, {alias}.stat_value6 StatValue6,
        {alias}.stat_type7 StatType7, {alias}.stat_value7 StatValue7,
        {alias}.stat_type8 StatType8, {alias}.stat_value8 StatValue8,
        {alias}.stat_type9 StatType9, {alias}.stat_value9 StatValue9,
        {alias}.stat_type10 StatType10, {alias}.stat_value10 StatValue10
        """;

    private sealed class CharacterProfessionRow
    {
        public uint Guid { get; init; }
        public uint AccountId { get; init; }
        public string Username { get; init; } = "";
        public string Name { get; init; } = "";
        public int Level { get; init; }
        public int Race { get; init; }
        public int CharacterClass { get; init; }
        public bool Online { get; init; }
        public ushort? SkillId { get; init; }
        public int CurrentSkill { get; init; }
        public int MaximumSkill { get; init; }
    }

    private sealed class KnownSpellRow
    {
        public uint Guid { get; init; }
        public uint SpellId { get; init; }
    }

    private sealed class EquippedCraftingItemRow : CraftingItemData
    {
        public int EquipmentSlot { get; init; }
    }

    private sealed class OwnedCraftingItemRow : CraftingItemData
    {
        public uint SourceCharacterGuid { get; init; }
        public string SourceCharacterName { get; init; } = "";
        public string SourceUsername { get; init; } = "";
        public string SourceLocation { get; init; } = "";
    }

    private sealed class ItemNameRow
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = "";
    }

    private sealed class TrainerRecipeRow
    {
        public ushort SkillId { get; init; }
        public uint SpellId { get; init; }
        public int RequiredSkill { get; init; }
    }

    private sealed class RecipeItemSourceRow
    {
        public uint RecipeItemId { get; init; }
        public string RecipeItemName { get; init; } = "";
        public ushort SkillId { get; init; }
        public int RequiredSkill { get; init; }
        public uint UseSpellId { get; init; }
    }

    private sealed class RecipeSourceNamesRow
    {
        public uint RecipeItemId { get; init; }
        public string SourceNames { get; init; } = "";
    }

    private sealed class MaterialCountRow
    {
        public uint Guid { get; init; }
        public uint ItemId { get; init; }
        public int Quantity { get; init; }
    }

    private sealed record RecipeSource(int RequiredSkill, string Description);
    private sealed record ProfessionTier(
        string Name, int TrainingSkill, int MaximumSkill, int CharacterLevel);
}
