namespace AzerothCore_UI.Web.Models;

public sealed record MissingProfessionSpell(
    ushort SkillId,
    string ProfessionName,
    uint SpellId,
    string? Name,
    string? Rank,
    byte RequiredLevel,
    ushort RequiredSkillRank,
    uint TrainingCost);
