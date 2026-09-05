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
    public void GiveButtonRequiresBothASelectedMountAndSelectedHeroes()
    {
        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        Assert.True(GiveButton(component).HasAttribute("disabled"));

        SelectMountRow(component, "Black Battlestrider");
        Assert.True(GiveButton(component).HasAttribute("disabled"));
    }

    [Fact]
    public async Task GivingTheSelectedMountSendsOneRequestPerSelectedHeroAndReportsSuccess()
    {
        var component = Render<Mounts>();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));

        var store = Services.GetRequiredService<SelectedCharacterStore>();
        await component.InvokeAsync(() =>
            store.SetSelectedAsync(["Vynlan", "Kiesh"], "Vynlan").AsTask());

        SelectMountRow(component, "Black Battlestrider");
        component.WaitForAssertion(() => Assert.False(GiveButton(component).HasAttribute("disabled")));

        GiveButton(component).Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, handler.GivenTo.Count);
            Assert.Contains("Vynlan", handler.GivenTo);
            Assert.Contains("Kiesh", handler.GivenTo);
            Assert.Contains("given to all 2 selected heroes", component.Markup);
        });
    }

    private static AngleSharp.Dom.IElement GiveButton(IRenderedComponent<Mounts> component) =>
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Give to selected heroes");

    private static void SelectMountRow(IRenderedComponent<Mounts> component, string mountName) =>
        component.FindAll("tbody tr").Single(row => row.TextContent.Contains(
                mountName, StringComparison.Ordinal))
            .QuerySelector("button")!.Click();

    private sealed class MountsHandler : HttpMessageHandler
    {
        public string? LastFaction { get; private set; }
        public List<string> GivenTo { get; } = [];

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
                    new(18243, "Black Battlestrider", 4, 40, 75, 262143, 1101,
                        "Alliance", null, null)
                };
                if (LastFaction is null)
                    mounts.Add(new(45725, "Argent Hippogryph", 4, 70, 300, -1, -1, null,
                        "Corporal Arthur Flew", null));
                return Json(new AdministrationMountSearchResult(mounts, 1, 30, mounts.Count, 1));
            }
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/server-administration/items/give")
            {
                var body = await request.Content!.ReadFromJsonAsync<GiveItemRequest>(
                    cancellationToken: cancellationToken);
                if (body?.PlayerName is not null) GivenTo.Add(body.PlayerName);
                return Json(new AdministrationResult(true, "Item given."));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
