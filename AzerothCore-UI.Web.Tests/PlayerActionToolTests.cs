using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Components.Shared;
using AzerothCore_UI.Web.Components.Shared.PlayerActions;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Security;
using AzerothCore_UI.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class PlayerActionToolTests : BunitContext
{
    private static readonly PlayerActionTarget OnlinePlayer =
        new("Jaina", true, false, "account");
    private static readonly PlayerActionTarget SecondOnlinePlayer =
        new("Thrall", true, false, "account");

    public PlayerActionToolTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(
            new ToolRequestHandler())
        {
            BaseAddress = new Uri("http://localhost")
        }));
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("world.creatures", "players.services");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(
                AdministrationPermissions.ClaimType,
                "world.creatures"),
            new Claim(
                AdministrationPermissions.ClaimType,
                "players.services"));
        Services.AddScoped<SelectedCharacterStore>();
        Services.AddScoped<RecentPickerSelectionStore>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ToolCollectionComposesAllReusableActionPanels()
    {
        var component = Render<PlayerActionTools>(parameters => parameters
            .Add(tools => tools.Targets, [OnlinePlayer]));

        component.WaitForAssertion(() => Assert.Equal(
            [
                "Give item",
                "Give money",
                "Teleport",
                "Movement speed",
                "Guild bank",
                "Summon a useful NPC",
                "Revive character",
                "Creature spawner"
            ],
            component.FindAll("h2")
                .Select(heading => heading.TextContent.Trim())
                .ToArray()));
    }

    [Fact]
    public async Task PlayerActionsSidebarUsesSharedHeaderTargetsInSingleColumnMode()
    {
        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await store.SetSelectedAsync(["Jaina", "Uther"], "Jaina");

        var component = Render<PlayerActionsSidebar>();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll(".character-picker"));
            var tools = component.FindComponent<PlayerActionTools>();
            Assert.True(tools.Instance.SingleColumn);
            Assert.Equal(
                ["Jaina", "Uther"],
                tools.Instance.Targets.Select(target => target.Name).ToArray());
            Assert.Contains("single-column",
                component.Find(".player-action-tools-grid").ClassList);
        });

        await component.InvokeAsync(() =>
            store.SetSelectedAsync(["Anduin"], "Anduin").AsTask());
        component.WaitForAssertion(() => Assert.Equal(
            ["Anduin"],
            component.FindComponent<PlayerActionTools>().Instance.Targets
                .Select(target => target.Name).ToArray()));
    }

    [Fact]
    public void CharacterPickerItemsFlowAcrossAndWrap()
    {
        var pickerItems = new[]
        {
            new CharacterPickerItem(
                OnlinePlayer.Name,
                OnlinePlayer.Name,
                "Level 20 Mage",
                OnlinePlayer.Online,
                OnlinePlayer.IsPlayerBot)
        };

        var component = Render<CharacterPicker>(parameters => parameters
            .Add(picker => picker.Title, "Action targets")
            .Add(picker => picker.Items, pickerItems)
            .Add(picker => picker.Multiple, true)
            .Add(picker => picker.SelectedValues,
                new HashSet<string>([OnlinePlayer.Name],
                    StringComparer.OrdinalIgnoreCase)));

        var itemContainer = component.Find(".character-picker-items");
        Assert.True(itemContainer.ClassList.Contains("d-flex"));
        Assert.True(itemContainer.ClassList.Contains("flex-wrap"));
        Assert.All(
            component.FindAll(".character-picker-item"),
            item => Assert.False(item.ClassList.Contains("w-100")));

        var header = component.Find(".character-picker-heading");
        Assert.Equal("Action targets", header.QuerySelector("h2")?.TextContent.Trim());
        var controls = header.QuerySelector(".character-picker-controls");
        Assert.NotNull(controls);
        Assert.Equal(2, controls.QuerySelectorAll("input[type='checkbox']").Length);
        Assert.Equal(
            ["Online", "Clear"],
            controls.QuerySelectorAll("button")
                .Select(button => button.TextContent.Trim())
                .ToArray());
    }

    [Fact]
    public void MoneyActionRequiresAnAvailableServerAndATarget()
    {
        var unavailable = Render<GiveMoneyTool>(parameters => parameters
            .Add(component => component.Targets, [OnlinePlayer])
            .Add(component => component.Available, false));
        var noTargets = Render<GiveMoneyTool>(parameters => parameters
            .Add(component => component.Targets, [])
            .Add(component => component.Available, true));

        Assert.True(SendButton(unavailable).HasAttribute("disabled"));
        Assert.True(SendButton(noTargets).HasAttribute("disabled"));
    }

    [Fact]
    public void MoneyActionIsEnabledForAnAvailableSelectedTarget()
    {
        var component = Render<GiveMoneyTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        Assert.False(SendButton(component).HasAttribute("disabled"));
    }

    [Fact]
    public void ReviveActionAppliesToEverySelectedCharacter()
    {
        var component = Render<ReviveCharacterTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer, SecondOnlinePlayer])
            .Add(tool => tool.Available, true));

        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Revive").Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Revive completed for all 2 selected characters.",
            component.Find("[role='status']").TextContent));
    }

    [Fact]
    public void HeaderIsTitleOnlyAndResultAppearsInTheFooterAfterExecution()
    {
        var component = Render<GiveMoneyTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        Assert.Equal("Give money",
            component.Find(".player-action-tool-heading h2").TextContent.Trim());
        Assert.Empty(component.FindAll("[role='status']"));

        SendButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.Find(".player-action-tool-heading")
                .QuerySelectorAll("[role='status']"));
            var footer = component.Find(".player-action-tool-footer");
            Assert.Contains(
                "Send money completed for all 1 selected characters.",
                footer.QuerySelector("[role='status']")?.TextContent);
        });
    }

    [Fact]
    public void ChangingTargetsPreservesSelectedItemsAndOtherToolInputs()
    {
        var component = Render<PlayerActionTools>(parameters => parameters
            .Add(tools => tools.Targets, [OnlinePlayer]));
        component.WaitForElement("#money-gold");

        component.Find("#money-gold").Change("12");
        component.Find("input.clickable-input").Click();
        component.WaitForElement("tr.picker-result-row").Click();
        component.WaitForAssertion(() => Assert.Contains(
            "Polished Breastplate (ID 2153)",
            component.FindAll("input").Single(input =>
                input.GetAttribute("value")?.Contains(
                    "Polished Breastplate", StringComparison.Ordinal) == true)
                .GetAttribute("value")));

        component.Render(parameters => parameters
            .Add(tools => tools.Targets, [OnlinePlayer, SecondOnlinePlayer]));

        Assert.Equal("12", component.Find("#money-gold").GetAttribute("value"));
        Assert.Contains(
            "Polished Breastplate (ID 2153)",
            component.FindAll("input").Single(input =>
                input.GetAttribute("value")?.Contains(
                    "Polished Breastplate", StringComparison.Ordinal) == true)
                .GetAttribute("value"));
    }

    [Fact]
    public void ItemPickerShowsRecentChoicesAndSelectsTheCurrentSearchTextOnReopen()
    {
        var component = Render<GiveItemTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        component.Find("input.clickable-input").Click();
        component.WaitForElement("tr.picker-result-row").Click();
        component.Find("input.clickable-input").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "Polished Breastplate",
                component.Find("#item-search").GetAttribute("value"));
            Assert.Contains(
                "Polished Breastplate (2153)",
                component.Find(".picker-recent-selections").TextContent);
        });
        Assert.Contains(JSInterop.Invocations, invocation =>
            invocation.Identifier == "azerothCoreUi.focusAndSelect");
    }

    [Fact]
    public void TeleportPlayerModeFiltersDefaultToOnlinePlayers()
    {
        var component = Render<TeleportTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Player").Click();
        component.WaitForElement("#movement-anchor");
        Assert.Equal(
            ["", "Anduin"],
            AnchorValues(component));

        component.Find("input[id$='-bots']").Change(true);
        Assert.Equal(
            ["", "Anduin", "Gennik"],
            AnchorValues(component));

        component.Find("input[id$='-offline']").Change(true);
        Assert.Equal(
            ["", "Anduin", "Gennik", "Uther", "Valeera"],
            AnchorValues(component));
    }

    [Fact]
    public void TeleportPopupPickersOpenFromClickableInputs()
    {
        var component = Render<TeleportTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        var locationInput = component.Find(
            "input[aria-label='Choose teleport location']");
        Assert.True(locationInput.ClassList.Contains("clickable-item"));
        Assert.DoesNotContain(
            component.FindAll("button"),
            button => button.TextContent.Contains(
                "Choose location", StringComparison.Ordinal));

        locationInput.Click();
        component.WaitForElement("#location-picker-title");
        component.Find("button[aria-label='Close']").Click();

        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "NPC").Click();
        var npcInput = component.Find("input[aria-label='Choose NPC']");
        Assert.True(npcInput.ClassList.Contains("clickable-item"));
        Assert.DoesNotContain(
            component.FindAll("button"),
            button => button.TextContent.Contains(
                "Choose NPC", StringComparison.Ordinal));

        npcInput.Click();
        component.WaitForElement("#npc-teleport-picker-title");
    }

    [Fact]
    public void TeleportCanRememberAndReturnSuccessfulTargets()
    {
        var component = Render<TeleportTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        Assert.True(component.Find("#remember-teleport-origin")
            .HasAttribute("checked"));
        component.Find("input[aria-label='Choose teleport location']").Click();
        component.WaitForElement("tr.picker-result-row").Click();
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Teleport to place").Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Return available for Jaina",
            component.Markup));
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Return").Click();
        component.WaitForAssertion(() => Assert.DoesNotContain(
            "Return available for Jaina",
            component.Markup));
    }

    [Fact]
    public void TeleportDoesNotOfferReturnWhenRememberingIsDisabled()
    {
        var component = Render<TeleportTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        component.Find("#remember-teleport-origin").Change(false);
        component.Find("input[aria-label='Choose teleport location']").Click();
        component.WaitForElement("tr.picker-result-row").Click();
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Teleport to place").Click();

        component.WaitForAssertion(() => Assert.DoesNotContain(
            component.FindAll("button"),
            button => button.TextContent.Trim() == "Return"));
    }

    [Fact]
    public void UtilityNpcInputsShareAnEightFourRowWithoutConfirmation()
    {
        var component = Render<UtilityNpcTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer])
            .Add(tool => tool.Available, true));

        var service = component.WaitForElement("#utility-npc");
        var despawn = component.Find("#utility-npc-despawn");

        Assert.True(service.ParentElement?.ClassList.Contains("col-8"));
        Assert.True(despawn.ParentElement?.ParentElement?.ClassList.Contains("col-4"));
        Assert.Empty(component.FindAll("#confirm-utility-npc"));
        Assert.False(component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Summon NPC").HasAttribute("disabled"));
    }

    [Fact]
    public void CreatureSpawnerUsesTheSharedTargetsAndACompactPopupPicker()
    {
        var component = Render<CreatureSpawnerTool>(parameters => parameters
            .Add(tool => tool.Targets, [OnlinePlayer, SecondOnlinePlayer])
            .Add(tool => tool.Available, true));

        var chooseCreature = component.Find(
            "input[aria-label='Choose creature']");
        Assert.True(chooseCreature.ClassList.Contains("clickable-item"));

        chooseCreature.Click();
        component.WaitForElement("#creature-picker-title");
        component.WaitForElement("tr.picker-result-row").Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Brown Bear (ID 123)",
            component.Find("input[aria-label='Choose creature']")
                .GetAttribute("value")));
        Assert.All(
            component.FindAll(
                "#creature-level, #creature-count, #creature-square-length, #creature-despawn"),
            input => Assert.True(
                input.ParentElement?.ClassList.Contains("col-3") == true
                || input.ParentElement?.ParentElement?.ClassList.Contains("col-3") == true));

        Assert.Empty(component.FindAll("#confirm-creature-spawn"));
        var spawnButton = component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Spawn 1");
        Assert.False(spawnButton.HasAttribute("disabled"));
        spawnButton.Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Spawn creature completed for all 2 selected characters.",
            component.Find("[role='status']").TextContent));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void GuildInspectionOnlyAcceptsAnOnlineRealPlayer(
        bool isBot,
        bool buttonDisabled)
    {
        var target = OnlinePlayer with { IsPlayerBot = isBot };
        var component = Render<GuildBankTool>(parameters => parameters
            .Add(tool => tool.Targets, [target])
            .Add(tool => tool.Available, true));

        Assert.Equal(
            buttonDisabled,
            InspectButton(component).HasAttribute("disabled"));
    }

    private static AngleSharp.Dom.IElement SendButton(
        IRenderedComponent<GiveMoneyTool> component) =>
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Send");

    private static AngleSharp.Dom.IElement InspectButton(
        IRenderedComponent<GuildBankTool> component) =>
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Inspect guild");

    private static string[] AnchorValues(
        IRenderedComponent<TeleportTool> component) =>
        component.FindAll("#movement-anchor option")
            .Select(option => option.GetAttribute("value") ?? "")
            .ToArray();

    private sealed class ToolRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            object body = request.RequestUri?.AbsolutePath switch
            {
                "/api/server-administration/availability" =>
                    new ToolAvailability(true, true, true),
                "/api/server-administration/players" =>
                    new AdministrationPlayer[]
                    {
                        new()
                        {
                            Name = "Jaina",
                            Username = "account",
                            Online = true,
                            Classification = "Player"
                        },
                        new()
                        {
                            Name = "Anduin",
                            Username = "family",
                            Online = true,
                            Classification = "Player"
                        },
                        new()
                        {
                            Name = "Uther",
                            Username = "family",
                            Online = false,
                            Classification = "Player"
                        },
                        new()
                        {
                            Name = "Gennik",
                            Username = "playerbots",
                            Online = true,
                            Classification = "PlayerBot"
                        },
                        new()
                        {
                            Name = "Valeera",
                            Username = "playerbots",
                            Online = false,
                            Classification = "PlayerBot"
                        }
                    },
                "/api/server-administration/items" =>
                    new AdministrationItemSearchResult(
                        [
                            new AdministrationItem
                            {
                                ItemId = 2153,
                                Name = "Polished Breastplate",
                                Quality = 2,
                                ItemLevel = 25,
                                RequiredLevel = 20,
                                SuitableTargetCount = 1,
                                TargetCount = 1
                            }
                        ],
                        1,
                        30,
                        1,
                        1),
                "/api/server-administration/creatures" =>
                    new AdministrationCreatureSearchResult(
                        [
                            new AdministrationCreature
                            {
                                CreatureId = 123,
                                Name = "Brown Bear",
                                MinimumLevel = 10,
                                MaximumLevel = 12,
                                CreatureType = 1,
                                Family = 4,
                                Tameable = true
                            }
                        ],
                        1,
                        30,
                        1,
                        1),
                "/api/server-administration/creatures/spawn" =>
                    new AdministrationResult(true, "Creature spawned."),
                "/api/server-administration/players/utility-npcs" =>
                    new UtilityNpc[]
                    {
                        new(
                            190001,
                            "Family Quartermaster",
                            "General supplies",
                            "Sells useful general goods.",
                            80)
                    },
                "/api/server-administration/teleport-locations" =>
                    new TeleportLocationSearchResult(
                        [new TeleportLocation { Id = 1, Name = "Orgrimmar", MapId = 1 }],
                        1, 30, 1, 1),
                "/api/server-administration/npc-teleports" =>
                    new NpcTeleportSearchResult([], 1, 30, 0, 0),
                "/api/server-administration/players/teleport" =>
                    new AdministrationResult(true, "Teleported."),
                "/api/server-administration/players/return" =>
                    new AdministrationResult(true, "Returned."),
                "/api/server-administration/money/give" =>
                    new AdministrationResult(true, "Money sent."),
                "/api/server-administration/characters/service" =>
                    new AdministrationResult(true, "Character revived."),
                _ => throw new InvalidOperationException(
                    $"Unexpected HTTP request in component test: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body)
            });
        }
    }
}
