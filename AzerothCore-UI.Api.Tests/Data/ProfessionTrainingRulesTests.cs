using AzerothCore_UI.Api.Data;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Data;

public sealed class ProfessionTrainingRulesTests
{
    [Fact]
    public void BuildUnlearnCommand_RemovesAllRanksFromTheApprenticeSpell()
    {
        var profession = ProfessionCatalog.All[171];

        Assert.Equal(
            "player unlearn Raistlin 2259 all",
            ProfessionTrainingRules.BuildUnlearnCommand("Raistlin", profession));
    }

    [Fact]
    public void ResolveLearnedSpellId_UsesSpellTaughtByTrainerWrapper()
    {
        var metadata = new SpellMetadata("Apprentice Alchemist", null, 2259);

        Assert.Equal(
            2259u,
            ProfessionTrainingRules.ResolveLearnedSpellId(2275, metadata));
    }

    [Fact]
    public void ResolveLearnedSpellId_UsesOriginalSpellWhenThereIsNoWrapperTarget()
    {
        var metadata = new SpellMetadata("Smelt Copper", null, null);

        Assert.Equal(
            2657u,
            ProfessionTrainingRules.ResolveLearnedSpellId(2657, metadata));
    }

    [Theory]
    [InlineData("Apprentice Alchemy", 75)]
    [InlineData("Journeyman Blacksmithing", 150)]
    [InlineData("Expert Tailoring", 225)]
    [InlineData("Artisan Engineering", 300)]
    [InlineData("Master Enchanting", 375)]
    [InlineData("Grand Master Inscription", 450)]
    public void IsRankAlreadyLearned_ReturnsTrueAtRankMaximum(
        string spellName,
        ushort currentMaximum)
    {
        Assert.True(ProfessionTrainingRules.IsRankAlreadyLearned(spellName, currentMaximum));
    }

    [Theory]
    [InlineData("Journeyman Alchemy", 75)]
    [InlineData("Grand Master Mining", 375)]
    [InlineData("Smelt Copper", 450)]
    [InlineData(null, 450)]
    public void IsRankAlreadyLearned_ReturnsFalseWhenTrainingIsStillRelevant(
        string? spellName,
        ushort currentMaximum)
    {
        Assert.False(ProfessionTrainingRules.IsRankAlreadyLearned(spellName, currentMaximum));
    }
}
