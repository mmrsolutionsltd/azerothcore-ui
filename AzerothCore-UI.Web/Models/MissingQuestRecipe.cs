namespace AzerothCore_UI.Web.Models;

public sealed record MissingQuestRecipe(
    ushort SkillId,
    string ProfessionName,
    uint QuestId,
    string QuestTitle,
    byte MinimumLevel,
    string RewardKind,
    uint ItemId,
    string ItemName,
    uint LearnedSpellId,
    string? RecipeName,
    ushort RequiredSkillRank);
