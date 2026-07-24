using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class DungeonReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_ReportsCompleteSuitableParty()
    {
        var party = new PartySnapshot("Leader", 5,
        [
            new("Leader", 22, "Tank", false),
            new("Heals", 21, "Healer", false),
            new("One", 20, "Damage", true),
            new("Two", 23, "DPS", true),
            new("Three", 24, "Damage", true)
        ], []);
        var dungeon = new DungeonDestination(1, "Dungeon", 20, 25, 33, "Normal");

        var result = DungeonReadinessEvaluator.Evaluate(party, dungeon, [], []);

        Assert.True(result.HasTank);
        Assert.True(result.HasHealer);
        Assert.Equal(3, result.DamageCount);
        Assert.True(result.PartyFull);
        Assert.True(result.LevelsSuitable);
    }

    [Fact]
    public void Evaluate_ReportsMissingRolesAndUnsuitableLevels()
    {
        var party = new PartySnapshot("Leader", 2,
        [
            new("Leader", 12, "Damage", false),
            new("Friend", 30, "Damage", false)
        ], []);
        var dungeon = new DungeonDestination(1, "Dungeon", 18, 24, 33, "Normal");

        var result = DungeonReadinessEvaluator.Evaluate(party, dungeon, [], []);

        Assert.False(result.HasTank);
        Assert.False(result.HasHealer);
        Assert.Equal(2, result.DamageCount);
        Assert.False(result.PartyFull);
        Assert.False(result.LevelsSuitable);
    }
}
