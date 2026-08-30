using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Components.Shared;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Security;
using AzerothCore_UI.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class RealmRosterHeaderTests : BunitContext
{
    private readonly RosterHandler handler = new();

    public RealmRosterHeaderTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.characters", "players.services");
        authorization.SetRoles("Owner");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdministrationPermissions.ClaimType, "players.characters"),
            new Claim(AdministrationPermissions.ClaimType, "players.services"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SelectedOnlineLeadersBecomeRichDistinctPartyCards()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        HeroChoice(component, "Vynlan").Click();
        HeroChoice(component, "Sarafel").Click();

        component.WaitForAssertion(() =>
        {
            var leaders = component.FindAll(".online-hero-card.leader");
            Assert.Equal(2, leaders.Count);
            Assert.Contains("Vynlan", leaders[0].TextContent);
            Assert.Contains("Sarafel", leaders[1].TextContent);
            Assert.All(leaders, leader => Assert.Contains("LEADER", leader.TextContent));
            Assert.Contains("1,250 HP", leaders[0].TextContent);
            Assert.Contains("DEAD", leaders[1].TextContent);
            Assert.Equal(2, component.FindAll(".selected-flag").Count);
        });
    }

    [Fact]
    public void PartyLeaderMovesToTheFirstSharedSelectionSlot()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        HeroChoice(component, "Kiesh").Click();
        HeroChoice(component, "Vynlan").Click();

        component.WaitForAssertion(() =>
        {
            var cards = component.FindAll(".online-hero-card");
            Assert.Equal(2, cards.Count);
            Assert.Contains("Vynlan", cards[0].TextContent);
            Assert.Contains("Kiesh", cards[1].TextContent);
            var selection = Services.GetRequiredService<SelectedCharacterStore>()
                .GetSelectedAsync().AsTask().GetAwaiter().GetResult();
            Assert.Equal(new[] { "Vynlan", "Kiesh" }, selection);
        });
    }

    [Fact]
    public void OfflineHeroesAreHiddenUntilTheCombinedHeaderFilterIsEnabled()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        Assert.DoesNotContain(component.FindAll(".hero-choice"), choice =>
            choice.TextContent.Contains("Offlinehero"));
        component.Find(".roster-selection-controls input[type='checkbox']")
            .Change(true);

        component.WaitForAssertion(() => Assert.Contains(
            component.FindAll(".hero-choice"), choice =>
                choice.TextContent.Contains("Offlinehero")));
    }

    [Fact]
    public void QuestingCompanionPageRevealsOfflineCompanionChoicesAutomatically()
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/questing-companions");

        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        component.WaitForAssertion(() => Assert.Contains(
            component.FindAll(".hero-choice"), choice =>
                choice.TextContent.Contains("Offlinehero")));
        Assert.True(component.Find(".roster-selection-controls input[type='checkbox']")
            .HasAttribute("checked"));
    }

    [Fact]
    public void PlayerBotsAreLoadedOnlyWhenTheCombinedHeaderFilterIsEnabled()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        Assert.DoesNotContain("Rndhelper", component.Markup);

        component.FindAll(".roster-selection-controls input[type='checkbox']")[1]
            .Change(true);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Rndhelper", component.Markup);
            Assert.Contains("BOT", component.Markup);
        });
    }

    [Fact]
    public void DoubleClickGestureSelectsTheFirstFiveVisibleOnlineTargets()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        HeroChoice(component, "Vynlan").TriggerEvent(
            "onclick", new MouseEventArgs { Detail = 2 });

        component.WaitForAssertion(() =>
        {
            Assert.Equal(5, component.FindAll(".online-hero-card").Count);
            Assert.Contains("5/5 selected", component.Markup);
        });
    }

    [Fact]
    public void RightClickingAHeroMakesItTheOnlySharedTarget()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();
        HeroChoice(component, "Sarafel").TriggerEvent(
            "oncontextmenu", new MouseEventArgs());

        component.WaitForAssertion(() =>
        {
            Assert.Single(component.FindAll(".online-hero-card"));
            Assert.Contains("Sarafel", component.Find(".online-hero-card").TextContent);
            Assert.Contains("1/5 selected", component.Markup);
        });
    }

    [Fact]
    public void ClickingRosterMemberUpdatesTheSharedActiveCharacter()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        HeroChoice(component, "Kiesh").Click();

        component.WaitForAssertion(() => Assert.Contains(
            "active", component.Find(".online-hero-card").ClassList));
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "localStorage.setItem"
            && invocation.Arguments.Any(argument => Equals(argument, "Kiesh")));
    }

    [Fact]
    public void FocusChangesTheActiveHeroWithoutDiscardingTheSelectedGroup()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();
        HeroChoice(component, "Sarafel").Click();

        component.FindAll(".focus-hero").Single(button =>
            button.ParentElement!.ParentElement!.TextContent.Contains("Vynlan"))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, component.FindAll(".online-hero-card").Count);
            Assert.Contains("Vynlan", component.Find(".online-hero-card.active").TextContent);
        });
    }

    [Fact]
    public void HeroRowStopsAtFiveCharacters()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");

        foreach (var name in new[] { "Vynlan", "Kiesh", "Sarafel", "Sarabeara", "Jaina" })
            HeroChoice(component, name).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(5, component.FindAll(".online-hero-card").Count);
            Assert.Contains("5/5 selected", component.Markup);
            Assert.True(HeroChoice(component, "Anduin").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void DeadOnlineHeroCanBeRevivedFromTheirCard()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Sarafel").Click();

        component.Find(".revive-button").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Sarafel", handler.RevivedCharacter);
            Assert.Contains("Character revived", component.Markup);
        });
    }

    [Fact]
    public void UpgradesPanelIsNotFetchedUntilToggled()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".online-hero-card")));
        Assert.Equal(0, handler.CraftingUpgradeRequestCount);
    }

    [Fact]
    public void TogglingUpgradesFetchesPlanOnceAndRendersSlots()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();
        component.WaitForElement(".upgrades-toggle");

        component.Find(".upgrades-toggle").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, handler.CraftingUpgradeRequestCount);
            var panel = component.Find(".hero-upgrade-panel");
            Assert.Contains("Head", panel.TextContent);
            Assert.Contains("Bolstered Helm", panel.TextContent);
            Assert.Empty(panel.QuerySelectorAll(".empty-vault"));
        });
    }

    [Fact]
    public void ReopeningUpgradesPanelUsesTheCachedPlanWithoutRefetching()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();
        component.WaitForElement(".upgrades-toggle");

        component.Find(".upgrades-toggle").Click();
        component.WaitForAssertion(() => Assert.Equal(1, handler.CraftingUpgradeRequestCount));

        component.Find(".upgrades-toggle").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".hero-upgrade-panel")));

        component.Find(".upgrades-toggle").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, handler.CraftingUpgradeRequestCount);
            Assert.Contains("Head", component.Find(".hero-upgrade-panel").TextContent);
        });
    }

    [Fact]
    public void TogglingTheBotsFilterDoesNotTriggerCraftingUpgradeRequests()
    {
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();
        component.WaitForElement(".upgrades-toggle");
        component.Find(".upgrades-toggle").Click();
        component.WaitForAssertion(() => Assert.Equal(1, handler.CraftingUpgradeRequestCount));

        component.FindAll(".roster-selection-controls input[type='checkbox']")[1].Change(true);

        component.WaitForAssertion(() => Assert.Contains("Rndhelper", component.Markup));
        Assert.Equal(1, handler.CraftingUpgradeRequestCount);
    }

    [Fact]
    public void LiveHealthUsesAPercentageAndTurnsRedBelowThirtyPercent()
    {
        handler.VynlanMaximumHealth = 5_000;
        var component = Render<RealmRosterHeader>();
        component.WaitForElement(".hero-choice");
        HeroChoice(component, "Vynlan").Click();

        component.WaitForAssertion(() =>
        {
            var health = component.Find(".health-track");
            Assert.Contains("25%", health.TextContent);
            Assert.Contains("low-health", health.ClassList);
            Assert.Contains("background-color:#c43b3f",
                health.QuerySelector("span")!.GetAttribute("style"));
        });
    }

    private static AngleSharp.Dom.IElement HeroChoice(
        IRenderedComponent<RealmRosterHeader> component, string name) =>
        component.FindAll(".hero-choice").Single(button =>
            button.TextContent.Contains(name, StringComparison.Ordinal));

    private sealed class RosterHandler : HttpMessageHandler
    {
        public string? RevivedCharacter { get; private set; }
        public uint? VynlanMaximumHealth { get; set; }
        public int CraftingUpgradeRequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath ==
                    "/api/server-administration/characters/service")
            {
                var body = await request.Content!.ReadFromJsonAsync<CharacterServiceRequest>(
                    cancellationToken: cancellationToken);
                RevivedCharacter = body?.PlayerName;
                return Json(new AdministrationResult(true, "Character revived."));
            }
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.StartsWith(
                    "/api/crafting-upgrades/", StringComparison.Ordinal))
            {
                CraftingUpgradeRequestCount++;
                return Json(new CraftingUpgradePlan(
                    new CraftingTargetCharacter(1, "Vynlan", "MARK", 20, 9, 10, true),
                    [],
                    [new CraftingGearSlot(0, "Head",
                        new CraftingGearItem(10, "Old Helm", 1, 10, 5, 1, 4, 1, []),
                        [new CraftingUpgradeRecommendation(
                            "CraftNow",
                            new CraftingGearItem(11, "Bolstered Helm", 3, 20, 15, 1, 4, 1, []),
                            true, true, null, "Crafter", "CrafterAcct", "Bags", null, null,
                            null, null, null, 0, null, "Recipe", "Known", [], [], [])])],
                    1, 1, 0, 0, "Test catalog"));
            }
            if (request.Method != HttpMethod.Get
                || request.RequestUri!.AbsolutePath != "/api/realm-roster")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            RealmRosterCharacter Player(
                uint guid, string name, string account, bool leader = false,
                bool companion = false, uint health = 900, bool online = true) =>
                new(guid, name, account, 20, 9, 10, online, leader, companion,
                    health, null);
            var vynlan = Player(1, "Vynlan", "MARK", true, health: 1250)
                with { MaximumHealth = VynlanMaximumHealth };
            var kiesh = Player(2, "Kiesh", "MARK2", companion: true);
            var sarafel = Player(3, "Sarafel", "SARA", true, health: 0);
            var sarabeara = Player(4, "Sarabeara", "SARA2", companion: true);
            RealmRosterCharacter[] heroes =
            [vynlan, kiesh, sarafel, sarabeara,
             Player(5, "Jaina", "MARK3"), Player(6, "Anduin", "MARK4"),
             Player(7, "Offlinehero", "MARK", online: false)];
            if (request.RequestUri.Query.Contains(
                    "includeBots=true", StringComparison.OrdinalIgnoreCase))
                heroes = [.. heroes,
                    Player(8, "Rndhelper", "RNDBOT42") with { IsPlayerBot = true }];
            return Json(new RealmRosterSnapshot(
                DateTime.UtcNow,
                [
                    new("group:1", 1, "Vynlan", true, true, 5, DateTime.UtcNow,
                        [vynlan, kiesh]),
                    new("group:2", 2, "Sarafel", true, true, 5, DateTime.UtcNow,
                        [sarafel, sarabeara])
                ],
                heroes[4..], [], heroes));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
