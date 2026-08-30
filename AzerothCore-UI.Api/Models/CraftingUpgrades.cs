namespace AzerothCore_UI.Api.Models;

public sealed record CraftingUpgradePlan(
    CraftingTargetCharacter Target,
    IReadOnlyList<CraftingProfessionSummary> Professions,
    IReadOnlyList<CraftingGearSlot> Slots,
    int OwnedUpgradeCount,
    int CraftNowCount,
    int LearnNextCount,
    int ProgressionCount,
    string DataSource);

public sealed record CraftingTargetCharacter(
    uint Guid, string Name, string Username, int Level,
    int Race, int CharacterClass, bool Online);

public sealed record CraftingProfessionSummary(
    uint CharacterGuid, string CharacterName, string Username,
    ushort SkillId, string ProfessionName, int CurrentSkill, int MaximumSkill);

public sealed record CraftingGearSlot(
    int Slot, string Name, CraftingGearItem? Equipped,
    IReadOnlyList<CraftingUpgradeRecommendation> Recommendations);

public sealed record CraftingGearItem(
    uint ItemId, string Name, int Quality, int ItemLevel,
    int RequiredLevel, int InventoryType, int ItemClass, int ItemSubclass,
    IReadOnlyList<CraftingGearStat> Stats);

public sealed record CraftingGearStat(string Name, double Value);

public sealed record CraftingStatDelta(
    string Name, double EquippedValue, double CandidateValue, double Difference);

public sealed record CraftingUpgradeRecommendation(
    string Availability,
    CraftingGearItem Item,
    bool UsableNow,
    bool PotentialUpgrade,
    uint? SourceCharacterGuid,
    string SourceCharacterName,
    string SourceUsername,
    string SourceLocation,
    ushort? ProfessionSkillId,
    string? ProfessionName,
    int? CurrentSkill,
    int? MaximumSkill,
    int? RequiredSkill,
    int SkillGap,
    uint? CraftSpellId,
    string RecipeName,
    string RecipeSource,
    IReadOnlyList<CraftingProgressionStep> ProgressionSteps,
    IReadOnlyList<CraftingMaterialRequirement> Materials,
    IReadOnlyList<CraftingStatDelta> StatDeltas);

public sealed record CraftingProgressionStep(
    int Order, string Kind, string Title, string Detail, bool Complete);

public sealed record CraftingMaterialRequirement(
    uint ItemId, string Name, int RequiredQuantity,
    int CrafterQuantity, int ScopedQuantity);
