using AzerothCore_UI.Api.Services;
using AzerothCore_UI.Api.Models;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class CraftingUpgradeRulesTests
{
    [Fact]
    public void ChoosesEmptySecondRingSlotBeforeReplacingFirstRing()
    {
        var ring = Item(inventoryType: 11, itemLevel: 22);
        var equipped = new Dictionary<int, CraftingItemData>
        {
            [10] = Item(inventoryType: 11, itemLevel: 10)
        };

        Assert.Equal(11, CraftingUpgradeRules.ChooseSlot(ring, equipped));
    }

    [Fact]
    public void ChoosesWeakerRingWhenBothRingSlotsAreOccupied()
    {
        var ring = Item(inventoryType: 11, itemLevel: 22);
        var equipped = new Dictionary<int, CraftingItemData>
        {
            [10] = Item(inventoryType: 11, itemLevel: 19),
            [11] = Item(inventoryType: 11, itemLevel: 8)
        };

        Assert.Equal(11, CraftingUpgradeRules.ChooseSlot(ring, equipped));
    }

    [Theory]
    [InlineData(5, 1, 20, true)]  // Priest, cloth.
    [InlineData(5, 2, 20, false)] // Priest, leather.
    [InlineData(3, 3, 1, true)]   // Hunter, mail from level 1 (WotLK proficiency).
    [InlineData(1, 4, 1, true)]   // Warrior, plate from level 1 (WotLK proficiency).
    [InlineData(2, 4, 1, true)]   // Paladin, plate from level 1 (WotLK proficiency).
    [InlineData(1, 1, 20, false)] // Warrior, cloth is not a sensible recommendation.
    [InlineData(1, 2, 20, false)] // Warrior, leather is not a sensible recommendation.
    [InlineData(3, 2, 20, false)] // Hunter, leather - should be offered mail instead.
    [InlineData(7, 1, 20, false)] // Shaman, cloth is not a sensible recommendation.
    [InlineData(6, 1, 20, false)] // Death Knight, cloth is not a sensible recommendation.
    [InlineData(6, 4, 20, true)]  // Death Knight, plate.
    public void AppliesClassArmorProficiencyRules(
        int characterClass, int armorSubclass, int level, bool expected)
    {
        var armor = Item(inventoryType: 5, itemLevel: 20,
            itemClass: 4, itemSubclass: armorSubclass);

        Assert.Equal(expected,
            CraftingUpgradeRules.IsUsable(armor, characterClass, race: 1, level));
    }

    [Fact]
    public void AppliesRequiredLevelAndExplicitClassMask()
    {
        var mageOnly = Item(inventoryType: 5, itemLevel: 30,
            requiredLevel: 20, itemClass: 4, itemSubclass: 1,
            allowableClass: 1L << (8 - 1));

        Assert.False(CraftingUpgradeRules.IsUsable(
            mageOnly, characterClass: 8, race: 1, level: 19));
        Assert.True(CraftingUpgradeRules.IsUsable(
            mageOnly, characterClass: 8, race: 1, level: 20));
        Assert.False(CraftingUpgradeRules.IsUsable(
            mageOnly, characterClass: 9, race: 1, level: 20));
    }

    [Fact]
    public void ProducesPositiveAndNegativeWowStyleStatDeltas()
    {
        var equipped = Item(inventoryType: 5, itemLevel: 10,
            armor: 40, statType1: 4, statValue1: 5);
        var candidate = Item(inventoryType: 5, itemLevel: 20,
            armor: 55, statType1: 4, statValue1: 2,
            statType2: 7, statValue2: 6);

        var deltas = CraftingUpgradeRules.Deltas(equipped, candidate);

        Assert.Contains(deltas, delta => delta.Name == "Armor" && delta.Difference == 15);
        Assert.Contains(deltas, delta => delta.Name == "Strength" && delta.Difference == -3);
        Assert.Contains(deltas, delta => delta.Name == "Stamina" && delta.Difference == 6);
    }

    [Fact]
    public void IncludesWeaponDpsInComparison()
    {
        var weapon = Item(inventoryType: 13, itemLevel: 20,
            itemClass: 2, itemSubclass: 7,
            minimumDamage: 20, maximumDamage: 30, delayMilliseconds: 2000);

        var result = CraftingUpgradeRules.ToGearItem(weapon);

        Assert.Contains(result.Stats,
            stat => stat.Name == "Weapon DPS" && Math.Abs(stat.Value - 12.5) < .01);
    }

    [Fact]
    public void BuildsOrderedSkillTrainingRecipeMaterialAndCraftingJourney()
    {
        var materials = new[]
        {
            new CraftingMaterialRequirement(2996, "Bolt of Linen Cloth", 8, 2, 5)
        };

        var steps = CraftingUpgradeService.BuildProgressionSteps(
            "Tailoring", characterLevel: 18, currentSkill: 100,
            maximumSkill: 150, requiredSkill: 240, knowsRecipe: false,
            "Buy the pattern from Ada", materials);

        Assert.Collection(steps,
            step => Assert.Equal("Raise Tailoring to 125", step.Title),
            step => Assert.Contains("Expert Tailoring", step.Title),
            step => Assert.Equal("Raise Tailoring to 200", step.Title),
            step => Assert.Contains("Artisan Tailoring", step.Title),
            step => Assert.Equal("Reach 240 Tailoring", step.Title),
            step => Assert.Equal("Obtain the recipe", step.Title),
            step => Assert.Contains("3 × Bolt of Linen Cloth", step.Detail),
            step => Assert.Equal("Create the upgrade", step.Title));
        Assert.Contains(steps, step => step.Detail.Contains("character level 20"));
    }

    private static CraftingItemData Item(
        int inventoryType, int itemLevel, int requiredLevel = 0,
        int itemClass = 4, int itemSubclass = 1,
        long allowableClass = -1, int armor = 0,
        int statType1 = 0, int statValue1 = 0,
        int statType2 = 0, int statValue2 = 0,
        double minimumDamage = 0, double maximumDamage = 0,
        int delayMilliseconds = 0) => new()
    {
        ItemId = 123,
        Name = "Test item",
        InventoryType = inventoryType,
        ItemLevel = itemLevel,
        RequiredLevel = requiredLevel,
        ItemClass = itemClass,
        ItemSubclass = itemSubclass,
        AllowableClass = allowableClass,
        AllowableRace = -1,
        Armor = armor,
        StatType1 = statType1,
        StatValue1 = statValue1,
        StatType2 = statType2,
        StatValue2 = statValue2,
        MinimumDamage = minimumDamage,
        MaximumDamage = maximumDamage,
        DelayMilliseconds = delayMilliseconds
    };
}
