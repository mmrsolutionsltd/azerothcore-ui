using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class StarterPresetPlannerTests
{
    [Fact]
    public void Plan_HunterGetsAppropriateHeirloomsAndArrows()
    {
        var actions = StarterPresetPlanner.Plan(
            "level10", 3, 10, true, new Dictionary<uint, int>(),
            4, true, true, true, true, 25);

        Assert.Contains(actions, action => action.ItemId == 42946 && action.Kind == "Heirloom");
        Assert.Contains(actions, action => action.ItemId == 44101 && action.Kind == "Heirloom");
        Assert.Contains(actions, action => action.ItemId == 48677 && action.Kind == "Heirloom");
        Assert.Contains(actions, action =>
            action.ItemId == 2515 && action.Quantity == 1000 && action.Delivery == "Direct");
        Assert.Equal(3, actions.Count(action => action.Kind == "Heirloom"));
    }

    [Fact]
    public void Plan_SkipsOwnedUniqueItemsAndOnlyAddsMissingBags()
    {
        var owned = new Dictionary<uint, int>
        {
            [42947] = 1,
            [StarterPresetPlanner.HearthstoneItemId] = 1,
            [StarterPresetPlanner.BagItemId] = 2
        };

        var actions = StarterPresetPlanner.Plan(
            "new", 8, 1, false, owned, 4, true, true, false, false, 0);

        Assert.True(actions.Single(action => action.ItemId == 42947).Skipped);
        Assert.True(actions.Single(action => action.ItemId == StarterPresetPlanner.HearthstoneItemId).Skipped);
        var bags = actions.Single(action => action.ItemId == StarterPresetPlanner.BagItemId);
        Assert.False(bags.Skipped);
        Assert.Equal(2, bags.Quantity);
        Assert.Equal("Mail", bags.Delivery);
    }

    [Fact]
    public void Plan_WarlockGetsShardsButOtherCasterDoesNot()
    {
        var warlock = StarterPresetPlanner.Plan(
            "new", 9, 10, true, new Dictionary<uint, int>(), 0, false, false, false, true, 0);
        var mage = StarterPresetPlanner.Plan(
            "new", 8, 10, true, new Dictionary<uint, int>(), 0, false, false, false, true, 0);

        Assert.Contains(warlock, action => action.ItemId == 6265 && action.Quantity == 10);
        Assert.DoesNotContain(mage, action => action.Kind == "Class supply");
    }

    [Fact]
    public void Plan_LowLevelHunterGetsUsableArrows()
    {
        var actions = StarterPresetPlanner.Plan(
            "new", 3, 1, true, new Dictionary<uint, int>(), 0, false, false, false, true, 0);

        Assert.Contains(actions, action => action.ItemId == 2512 && action.Description == "Rough Arrow");
        Assert.DoesNotContain(actions, action => action.ItemId == 2515);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(5, 10)]
    [InlineData(4, 1001)]
    public void Validate_RejectsUnsafeLimits(int bagCount, int moneyGold)
    {
        var request = new StarterPresetRequest(
            ["Hundead"], "new", bagCount, true, true, true, true, moneyGold, false);

        Assert.Throws<ArgumentException>(() => StarterPresetPlanner.Validate(request));
    }
}
