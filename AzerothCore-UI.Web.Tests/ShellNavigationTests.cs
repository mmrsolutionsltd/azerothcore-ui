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

        Assert.Equal(3, tabs.FindAll(".command-tab").Count);
        Assert.Contains("Gearing Room", tabs.Find(".command-tab.active").TextContent);
        Assert.Equal("crafting-upgrades",
            tabs.FindAll(".command-tab")[0].GetAttribute("href"));
    }

    [Fact]
    public void CommandTabsHideFeaturesTheSignedInUserCannotUse()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("quester");
        authorization.SetPolicies("adventures.quests");

        var tabs = Render<RealmCommandTabs>();

        Assert.Single(tabs.FindAll(".command-tab"));
        Assert.Contains("Adventures", tabs.Markup);
    }
}
