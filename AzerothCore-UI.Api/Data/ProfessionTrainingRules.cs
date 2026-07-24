namespace AzerothCore_UI.Api.Data;

public static class ProfessionTrainingRules
{
    public static string BuildUnlearnCommand(
        string playerName,
        ProfessionDefinition profession) =>
        $"player unlearn {playerName} {profession.ApprenticeSpellId} all";

    public static uint ResolveLearnedSpellId(uint trainerSpellId, SpellMetadata? metadata) =>
        metadata?.LearnedSpellId ?? trainerSpellId;

    public static bool IsRankAlreadyLearned(string? spellName, ushort currentMaximum)
    {
        var learnedMaximum = spellName switch
        {
            not null when spellName.StartsWith("Apprentice ", StringComparison.Ordinal) => 75,
            not null when spellName.StartsWith("Journeyman ", StringComparison.Ordinal) => 150,
            not null when spellName.StartsWith("Expert ", StringComparison.Ordinal) => 225,
            not null when spellName.StartsWith("Artisan ", StringComparison.Ordinal) => 300,
            not null when spellName.StartsWith("Master ", StringComparison.Ordinal) => 375,
            not null when spellName.StartsWith("Grand Master ", StringComparison.Ordinal) => 450,
            _ => 0
        };

        return learnedMaximum > 0 && currentMaximum >= learnedMaximum;
    }
}
