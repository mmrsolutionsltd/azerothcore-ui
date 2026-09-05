using AzerothCore_UI.Web.Components.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class ShellNavigationTests : BunitContext
{
    [Fact]
    public void CommandTabsAreRealAuthorizedRoutesAndHighlightTheCurrentPage()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.characters",
            "adventures.quests", "players.services");
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/crafting-upgrades");

        var tabs = Render<RealmCommandTabs>();

        Assert.Equal(17, tabs.FindAll(".command-tab").Count);
        var activeTab = tabs.Find(".command-tab.active");
        Assert.Equal("Gearing room", activeTab.TextContent.Trim());
        Assert.Equal("crafting-upgrades", activeTab.GetAttribute("href"));
    }

    [Fact]
    public void CommandTabsHideFeaturesTheSignedInUserCannotUse()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("quester");
        authorization.SetPolicies("adventures.quests");

        var tabs = Render<RealmCommandTabs>();

        Assert.Equal(7, tabs.FindAll(".command-tab").Count);
        Assert.Contains("Adventures", tabs.Markup);
    }
}
