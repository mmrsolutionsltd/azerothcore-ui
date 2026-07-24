namespace AzerothCore_UI.Api.Data;

public static class ProfessionPairingGuide
{
    private static readonly IReadOnlyDictionary<ushort, ushort[]> Pairings =
        new Dictionary<ushort, ushort[]>
        {
            [164] = [186],
            [165] = [393],
            [171] = [182],
            [182] = [171, 773],
            [186] = [164, 202, 755],
            [197] = [333],
            [202] = [186],
            [333] = [197],
            [393] = [165],
            [755] = [186],
            [773] = [182]
        };

    public static IReadOnlyList<ProfessionDefinition> GetPairings(ushort skillId) =>
        Pairings.TryGetValue(skillId, out var pairedSkillIds)
            ? pairedSkillIds.Select(id => ProfessionCatalog.All[id]).ToArray()
            : [];
}
