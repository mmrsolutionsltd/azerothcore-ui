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

public sealed class CraftingUpgradePlannerTests : BunitContext
{
    private readonly PlannerHandler handler = new();

    public CraftingUpgradePlannerTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.characters");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdministrationPermissions.ClaimType, "players.characters"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task SettingTheActiveHeroLoadsThePlanAndShowsOnlyUpgradeableSlots()
    {
        var store = Services.GetRequiredService<SelectedCharacterStore>();
        var component = Render<CraftingUpgradePlanner>();

        await component.InvokeAsync(() => store.SetAsync("Vynlan").AsTask());

        component.WaitForAssertion(() =>
        {
            Assert.Single(component.FindAll(".gear-slot"));
            Assert.Contains("Bolstered Helm", component.Markup);
            Assert.DoesNotContain("Empty Chest", component.Markup);
        });
    }

    [Fact]
    public async Task ActivatingAHeroOutsideThisAccountClearsTheStalePlanInstead()
    {
        var store = Services.GetRequiredService<SelectedCharacterStore>();
        var component = Render<CraftingUpgradePlanner>();
        await component.InvokeAsync(() => store.SetAsync("Vynlan").AsTask());
        component.WaitForAssertion(() => Assert.Contains("Bolstered Helm", component.Markup));

        await component.InvokeAsync(() => store.SetAsync("Rndhelper").AsTask());

        component.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Bolstered Helm", component.Markup);
            Assert.Contains("Rndhelper is not available in the Artisan Gearing Room.",
                component.Markup);
        });
    }

    private sealed class PlannerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath == "/api/characters")
            {
                return Task.FromResult(Json(new[]
                {
                    new CharacterOverviewSummary(1, "MARK", "Vynlan", 20, 1, 9, true,
                        0, 0, 0, 0, "Elwynn Forest", 0, 0, null, null, null)
                }));
            }
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.StartsWith(
                    "/api/crafting-upgrades/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(new CraftingUpgradePlan(
                    new CraftingTargetCharacter(1, "Vynlan", "MARK", 20, 9, 10, true),
                    [],
                    [
                        new CraftingGearSlot(0, "Head",
                            new CraftingGearItem(10, "Old Helm", 1, 10, 5, 1, 4, 1, []),
                            [new CraftingUpgradeRecommendation(
                                "CraftNow",
                                new CraftingGearItem(11, "Bolstered Helm", 3, 20, 15, 1, 4, 1, []),
                                true, true, null, "Crafter", "CrafterAcct", "Bags", null, null,
                                null, null, null, 0, null, "Recipe", "Known", [], [], [])]),
                        new CraftingGearSlot(4, "Chest",
                            new CraftingGearItem(12, "Empty Chest", 1, 10, 5, 1, 4, 1, []),
                            [])
                    ],
                    1, 1, 0, 0, "Test catalog")));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
