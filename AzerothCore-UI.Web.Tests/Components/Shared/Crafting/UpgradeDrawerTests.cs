using AzerothCore_UI.Web.Components.Shared.Crafting;
using AzerothCore_UI.Web.Models;
using Bunit;
using Xunit;

namespace AzerothCore_UI.Web.Tests.Components.Shared.Crafting;

public sealed class UpgradeDrawerTests : BunitContext
{
    private static readonly CraftingGearSlot SlotWithOneRecommendation = new(
        0, "Head",
        new CraftingGearItem(1, "Old Helm", 1, 10, 5, 1, 4, 1, []),
        [new CraftingUpgradeRecommendation(
            "CraftNow", new CraftingGearItem(2, "Better Helm", 3, 20, 15, 1, 4, 1, []),
            true, true, null, "Crafter", "CrafterAcct", "Bags", null, null,
            null, null, null, 0, null, "Recipe", "Known", [], [], [])]);

    [Fact]
    public void DefaultAllFilterShowsExistingRecommendations()
    {
        var component = Render<UpgradeDrawer>(parameters => parameters
            .Add(p => p.Slot, SlotWithOneRecommendation));

        Assert.Empty(component.FindAll(".empty-vault"));
        Assert.Single(component.FindAll(".upgrade-card"));
        Assert.Contains("Better Helm", component.Markup);
    }

    [Fact]
    public void NonMatchingFilterHidesRecommendations()
    {
        var component = Render<UpgradeDrawer>(parameters => parameters
            .Add(p => p.Slot, SlotWithOneRecommendation)
            .Add(p => p.Filter, "LearnNext"));

        Assert.Empty(component.FindAll(".upgrade-card"));
        Assert.Single(component.FindAll(".empty-vault"));
    }

    [Fact]
    public void ClickingAFilterButtonInvokesFilterChanged()
    {
        string? selected = null;
        var component = Render<UpgradeDrawer>(parameters => parameters
            .Add(p => p.Slot, SlotWithOneRecommendation)
            .Add(p => p.FilterChanged, filter => selected = filter));

        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Craft now").Click();

        Assert.Equal("CraftNow", selected);
    }
}
