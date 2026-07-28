namespace AzerothCore_UI.Api.Data;

public static class ProfessionCatalog
{
    public const int MaximumPrimaryProfessions = 2;

    public static readonly IReadOnlyDictionary<ushort, ProfessionDefinition> All =
        new Dictionary<ushort, ProfessionDefinition>
        {
            [129] = new(129, "First Aid", "Secondary", 3273, 0),
            [164] = new(164, "Blacksmithing", "Primary", 2018, 0),
            [165] = new(165, "Leatherworking", "Primary", 2108, 0),
            [171] = new(171, "Alchemy", "Primary", 2259, 0),
            [182] = new(182, "Herbalism", "Primary", 2366, 0),
            [185] = new(185, "Cooking", "Secondary", 2550, 0),
            [186] = new(186, "Mining", "Primary", 2575, 0),
            [197] = new(197, "Tailoring", "Primary", 3908, 0),
            [202] = new(202, "Engineering", "Primary", 4036, 0),
            [333] = new(333, "Enchanting", "Primary", 7411, 0),
            [356] = new(356, "Fishing", "Secondary", 7620, 0),
            [393] = new(393, "Skinning", "Primary", 8613, 0),
            [755] = new(755, "Jewelcrafting", "Primary", 25229, 0),
            [773] = new(773, "Inscription", "Primary", 45357, 0)
        };

    public static bool CanLearn(
        ProfessionDefinition profession,
        byte characterLevel,
        IReadOnlySet<ushort> knownSkillIds)
    {
        if (knownSkillIds.Contains(profession.SkillId) || characterLevel < profession.RequiredLevel)
            return false;

        return profession.Category != "Primary"
            || knownSkillIds.Count(skillId =>
                All.TryGetValue(skillId, out var known) && known.Category == "Primary")
                < MaximumPrimaryProfessions;
    }
}

public sealed record ProfessionDefinition(
    ushort SkillId,
    string Name,
    string Category,
    uint ApprenticeSpellId,
    byte RequiredLevel);
