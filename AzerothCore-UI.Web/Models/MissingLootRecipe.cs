namespace AzerothCore_UI.Web.Models;

public sealed record MissingLootRecipe(ushort SkillId, string ProfessionName, uint ItemId,
    string ItemName, uint LearnedSpellId, string? RecipeName, ushort RequiredSkillRank,
    string SourceType, string SourceNames, float? DropChance);

public sealed record MissingUnclassifiedRecipe(ushort SkillId, string ProfessionName, uint ItemId,
    string ItemName, uint LearnedSpellId, string? RecipeName, ushort RequiredSkillRank);
