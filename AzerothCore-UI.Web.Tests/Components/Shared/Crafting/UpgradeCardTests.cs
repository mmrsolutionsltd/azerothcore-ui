using AzerothCore_UI.Web.Components.Shared.Crafting;
using AzerothCore_UI.Web.Models;
using Bunit;
using Xunit;

namespace AzerothCore_UI.Web.Tests.Components.Shared.Crafting;

public sealed class UpgradeCardTests : BunitContext
{
    [Fact]
    public void RendersAvailabilityBadgeQualityAndStatDeltas()
    {
        var recommendation = new CraftingUpgradeRecommendation(
            "CraftNow",
            new CraftingGearItem(10, "Blessed Helm", 4, 55, 40, 1, 4, 4,
                [new CraftingGearStat("Strength", 12)]),
            true, true, 5, "Crafter", "CrafterAcct", "Bags", 197, "Blacksmithing",
            250, 300, 200, 0, 999, "Blessed Helm", "Known recipe", [], [],
            [new CraftingStatDelta("Strength", 5, 12, 7)]);

        var component = Render<UpgradeCard>(parameters => parameters
            .Add(p => p.Recommendation, recommendation)
            .Add(p => p.SlotId, 0));

        Assert.Contains("availability-craftnow", component.Find(".upgrade-card").ClassList);
        Assert.Contains("quality-4", component.Find(".upgrade-card").ClassList);
        Assert.Equal("Craft now", component.Find(".availability-badge").TextContent);
        Assert.Contains("+7 Strength", component.Find(".stat-comparison").TextContent);
    }

    [Fact]
    public void ShowsComparableCoreStatsWhenNoMeaningfulDeltaExists()
    {
        var recommendation = new CraftingUpgradeRecommendation(
            "Owned", new CraftingGearItem(10, "Plain Helm", 1, 20, 15, 1, 4, 1, []),
            true, false, null, "Bags", "Owner", "Bank", null, null,
            null, null, null, 0, null, "N/A", "Already owned", [], [], []);

        var component = Render<UpgradeCard>(parameters => parameters
            .Add(p => p.Recommendation, recommendation)
            .Add(p => p.SlotId, 0));

        Assert.Contains("Comparable core stats", component.Find(".stat-comparison").TextContent);
    }
}
