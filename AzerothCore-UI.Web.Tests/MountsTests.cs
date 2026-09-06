using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Components.Pages;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Security;
using AzerothCore_UI.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class MountsTests : BunitContext
{
    private readonly MountsHandler handler = new();

    public MountsTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.actions");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdministrationPermissions.ClaimType, "players.actions"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LoadsAndRendersTheMountCatalogue()
    {
        var component = Render<Mounts>();

        component.WaitForAssertion(() =>
        {
            var rows = component.FindAll("tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Black Battlestrider", component.Markup);
            Assert.Contains("Argent Hippogryph", component.Markup);
        });
    }

    [Fact]
    public void ChangingTheFactionFilterRequestsAFilteredCatalogue()
    {
        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        component.Find("select:has(option[value='Alliance'])").Change("Alliance");

        component.WaitForAssertion(() => Assert.Equal("Alliance", handler.LastFaction));
    }

    [Fact]
    public void GiveButtonRequiresAtLeastOneSelectedMountAndSelectedHeroes()
    {
        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        Assert.True(GiveButton(component).HasAttribute("disabled"));

        ToggleMountRow(component, "Black Battlestrider");
        Assert.True(GiveButton(component).HasAttribute("disabled"));
    }

    [Fact]
    public async Task SelectingMultipleMountsSendsOneRequestPerMountPerSelectedHero()
    {
        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() =>
            store.SetSelectedAsync(["Vynlan", "Kiesh"], "Vynlan").AsTask());

        ToggleMountRow(component, "Black Battlestrider");
        ToggleMountRow(component, "Argent Hippogryph");
        component.WaitForAssertion(() => Assert.False(GiveButton(component).HasAttribute("disabled")));
        Assert.Contains("4 total transfers", component.Markup);

        GiveButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(4, handler.GivenItemIds.Count);
            Assert.Equal(2, handler.GivenItemIds.Count(id => id == 18243));
            Assert.Equal(2, handler.GivenItemIds.Count(id => id == 45725));
            Assert.Contains("Vynlan", handler.GivenTo);
            Assert.Contains("Kiesh", handler.GivenTo);
            Assert.Contains("Gave 2 mounts to 2 heroes (4 total)", component.Markup);
        });
    }

    [Fact]
    public async Task ClearingSelectionRemovesAllSelectedMounts()
    {
        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() => store.SetSelectedAsync(["Vynlan"], "Vynlan").AsTask());

        ToggleMountRow(component, "Black Battlestrider");
        component.WaitForAssertion(() => Assert.False(GiveButton(component).HasAttribute("disabled")));

        component.FindAll("button").Single(button => button.TextContent.Trim() == "Clear").Click();

        Assert.True(GiveButton(component).HasAttribute("disabled"));
        Assert.Contains("Select one or more mounts", component.Markup);
    }

    [Fact]
    public async Task ACrossFactionMountIsAlwaysGivenRegardlessOfTheAcknowledgementCheckbox()
    {
        handler.HeroStatusesByMount[18243] =
            [new MountHeroStatus("Kiesh", true, false, 0, 0, "", true, 0)];

        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() => store.SetSelectedAsync(["Kiesh"], "Kiesh").AsTask());

        ToggleMountRow(component, "Black Battlestrider");
        component.WaitForAssertion(() => Assert.False(GiveButton(component).HasAttribute("disabled")));

        GiveButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Single(handler.GivenTo);
            Assert.Contains("Kiesh", handler.GivenTo);
            Assert.False(handler.CrossFactionOverrideByPlayer.TryGetValue("Kiesh", out var overrideFlag) && overrideFlag);
            Assert.Contains("cross-faction mount", component.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CheckingTheAcknowledgementBoxRecordsTheOverrideOnAMismatchedGive()
    {
        handler.HeroStatusesByMount[18243] =
            [new MountHeroStatus("Kiesh", true, false, 0, 0, "", true, 0)];

        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() => store.SetSelectedAsync(["Kiesh"], "Kiesh").AsTask());

        ToggleMountRow(component, "Black Battlestrider");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("#allowCrossFaction")));
        component.Find("#allowCrossFaction").Change(true);

        GiveButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Single(handler.GivenTo);
            Assert.True(handler.CrossFactionOverrideByPlayer.TryGetValue("Kiesh", out var overrideFlag) && overrideFlag);
        });
    }

    [Fact]
    public void MountsWithNoReputationRequirementShowAnEmDash()
    {
        var component = Render<Mounts>();

        component.WaitForAssertion(() =>
        {
            var hippogryphRow = component.FindAll("tbody tr")
                .Single(row => row.TextContent.Contains("Argent Hippogryph", StringComparison.Ordinal));
            Assert.Contains("—", hippogryphRow.TextContent);
        });
    }

    [Fact]
    public async Task MountRowsShowPerHeroReputationStatusForSelectedHeroes()
    {
        handler.HeroStatusesByMount[18243] =
        [
            new MountHeroStatus("Vynlan", false, false, 45000, 7, "Exalted", true, 0),
            new MountHeroStatus("Kiesh", false, false, 1000, 3, "Neutral", false, 20000)
        ];

        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() =>
            store.SetSelectedAsync(["Vynlan", "Kiesh"], "Vynlan").AsTask());
        ToggleMountRow(component, "Black Battlestrider");

        component.WaitForAssertion(() =>
        {
            var row = component.FindAll("tbody tr")
                .Single(r => r.TextContent.Contains("Black Battlestrider", StringComparison.Ordinal));
            Assert.Contains("Vynlan: ready", row.TextContent);
            Assert.Contains("Kiesh: needs 20,000 more", row.TextContent);
        });
    }

    [Fact]
    public async Task MountsAlreadyKnownByASelectedHeroAreHighlightedWithoutBlockingAnotherGive()
    {
        handler.HeroStatusesByMount[45725] =
            [new MountHeroStatus("Vynlan", false, true, 0, 0, "", true, 0)];

        var component = Render<Mounts>();
        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() => store.SetSelectedAsync(["Vynlan"], "Vynlan").AsTask());

        component.WaitForAssertion(() =>
        {
            var row = component.FindAll("tbody tr")
                .Single(r => r.TextContent.Contains("Argent Hippogryph", StringComparison.Ordinal));
            Assert.Contains("Known: Vynlan", row.TextContent);
        });

        ToggleMountRow(component, "Argent Hippogryph");
        component.WaitForAssertion(() => Assert.False(GiveButton(component).HasAttribute("disabled")));
    }

    private static AngleSharp.Dom.IElement GiveButton(IRenderedComponent<Mounts> component) =>
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Give to selected heroes");

    private static void ToggleMountRow(IRenderedComponent<Mounts> component, string mountName) =>
        component.FindAll("tbody tr").Single(row => row.TextContent.Contains(
                mountName, StringComparison.Ordinal))
            .QuerySelector("input[type=checkbox]")!.Change(true);

    private sealed class MountsHandler : HttpMessageHandler
    {
        public string? LastFaction { get; private set; }
        public List<string> GivenTo { get; } = [];
        public List<uint> GivenItemIds { get; } = [];
        public Dictionary<string, bool> CrossFactionOverrideByPlayer { get; } = [];
        public Dictionary<uint, IReadOnlyList<MountHeroStatus>> HeroStatusesByMount { get; } = [];

        private IReadOnlyList<MountHeroStatus> HeroStatusesFor(uint itemId) =>
            HeroStatusesByMount.TryGetValue(itemId, out var statuses) ? statuses : [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath == "/api/server-administration/mounts")
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                LastFaction = string.IsNullOrEmpty(query["faction"]) ? null : query["faction"];
                var mounts = new List<AdministrationMount>
                {
                    new(18243, "Black Battlestrider", 4, 40, 75, 262143, 1101, "Alliance", null, null,
                        RequiredFactionId: 69, RequiredFactionName: "Stormwind",
                        RequiredReputationRank: 6, SpellId: 1, HeroStatuses: HeroStatusesFor(18243))
                };
                if (LastFaction is null)
                    mounts.Add(new(45725, "Argent Hippogryph", 4, 70, 300, -1, -1, null,
                        "Corporal Arthur Flew", null,
                        RequiredFactionId: 0, RequiredFactionName: null,
                        RequiredReputationRank: 0, SpellId: 2, HeroStatuses: HeroStatusesFor(45725)));
                return Json(new AdministrationMountSearchResult(mounts, 1, 30, mounts.Count, 1));
            }
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/server-administration/items/give")
            {
                var body = await request.Content!.ReadFromJsonAsync<GiveItemRequest>(
                    cancellationToken: cancellationToken);
                if (body?.PlayerName is not null)
                {
                    GivenTo.Add(body.PlayerName);
                    GivenItemIds.Add(body.ItemId);
                    CrossFactionOverrideByPlayer[body.PlayerName] = body.CrossFactionOverride;
                }
                return Json(new AdministrationResult(true, "Item given."));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
