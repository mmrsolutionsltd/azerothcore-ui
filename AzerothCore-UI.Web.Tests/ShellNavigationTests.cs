using AzerothCore_UI.Web.Components.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class ShellNavigationTests : BunitContext
{
    [Fact]
    public void NavMenuHighlightsTheCurrentPageAmongTheMigratedToolLinks()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.characters",
            "adventures.quests", "players.services");
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/crafting-upgrades");

        var menu = Render<NavMenu>();

        Assert.Equal(
            ["Gearing room", "Trainer finder", "Profession training", "Weapon training"],
            GroupLinks(menu, "gearing-room")
                .Select(link => link.TextContent.Trim())
                .ToArray());
        var activeLink = menu.Find(".nav-link.active");
        Assert.Equal("Gearing room", activeLink.TextContent.Trim());
        Assert.Equal("crafting-upgrades", activeLink.GetAttribute("href"));
    }

    [Fact]
    public void NavMenuHidesToolGroupsTheSignedInUserCannotUse()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("quester");
        authorization.SetPolicies("adventures.quests");

        var menu = Render<NavMenu>();

        Assert.Empty(menu.FindAll("[data-nav-group='gearing-room']"));
        Assert.Empty(menu.FindAll("[data-nav-group='character-services']"));
        var adventures = menu.Find("[data-nav-group='adventures']");
        Assert.Equal(
            ["Quest helper", "Questing companions", "Companion commands",
                "Companion diagnostics", "Dungeon library", "Dungeon assistant", "Client addons"],
            adventures.QuerySelectorAll(".nav-link")
                .Select(link => link.TextContent.Trim())
                .ToArray());
    }

    [Fact]
    public void CharactersIsNotDuplicatedBetweenTheQuickLinkAndTheGearingRoomGroup()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.characters");

        var menu = Render<NavMenu>();

        Assert.Single(menu.FindAll(".nav-link"),
            link => link.TextContent.Trim() == "Characters");
    }

    private static AngleSharp.Dom.IElement[] GroupLinks(
        IRenderedComponent<NavMenu> menu, string groupKey) =>
        menu.Find($"[data-nav-group='{groupKey}']")
            .QuerySelectorAll(".nav-link")
            .ToArray();
}
