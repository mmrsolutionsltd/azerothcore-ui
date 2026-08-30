using AzerothCore_UI.Web.Components.Shared.Crafting;
using AzerothCore_UI.Web.Models;
using Bunit;
using Xunit;

namespace AzerothCore_UI.Web.Tests.Components.Shared.Crafting;

public sealed class GearSlotButtonTests : BunitContext
{
    [Fact]
    public void RendersNothingWhenSlotIsMissing()
    {
        var component = Render<GearSlotButton>(parameters => parameters
            .Add(p => p.SlotId, 0));

        Assert.Equal("", component.Markup.Trim());
    }

    [Fact]
    public void RendersSlotNameEquippedItemAndUpgradeCount()
    {
        var slot = new CraftingGearSlot(0, "Head",
            new CraftingGearItem(1, "Test Helm", 3, 40, 35, 1, 4, 3, []),
            [new CraftingUpgradeRecommendation(
                "CraftNow", new CraftingGearItem(2, "Better Helm", 4, 50, 40, 1, 4, 3, []),
                true, true, null, "Crafter", "CrafterAcct", "Bags", null, null,
                null, null, null, 0, null, "Recipe", "Known", [], [], [])]);

        var component = Render<GearSlotButton>(parameters => parameters
            .Add(p => p.SlotId, 0)
            .Add(p => p.Slot, slot));

        Assert.Contains("Head", component.Markup);
        Assert.Contains("Test Helm", component.Markup);
        Assert.Equal("1", component.Find(".upgrade-count").TextContent);
    }

    [Fact]
    public void AppliesSelectedClassAndInvokesCallback()
    {
        var slot = new CraftingGearSlot(0, "Head", null, []);
        var clicked = false;
        var component = Render<GearSlotButton>(parameters => parameters
            .Add(p => p.SlotId, 0)
            .Add(p => p.Slot, slot)
            .Add(p => p.Selected, true)
            .Add(p => p.OnSelected, () => clicked = true));

        Assert.Contains("selected", component.Find(".gear-slot").ClassList);
        component.Find(".gear-slot").Click();
        Assert.True(clicked);
    }
}
