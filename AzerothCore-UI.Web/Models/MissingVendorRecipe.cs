namespace AzerothCore_UI.Web.Models;

public sealed record MissingVendorRecipe(
    ushort SkillId,
    string ProfessionName,
    uint ItemId,
    string ItemName,
    uint LearnedSpellId,
    string? RecipeName,
    ushort RequiredSkillRank,
    uint BuyPrice,
    bool UsesExtendedCost,
    int VendorCount,
    string VendorNames,
    ushort RequiredFactionId,
    string? FactionName,
    byte RequiredReputationRank,
    int CurrentStanding,
    bool ReputationRequirementMet);
