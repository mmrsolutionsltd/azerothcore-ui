using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Components.Pages;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Security;
using AzerothCore_UI.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class QuestingCompanionsTests : BunitContext
{
    private readonly CompanionHandler handler = new();

    public QuestingCompanionsTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("adventures.quests");
        authorization.SetRoles("Owner");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdministrationPermissions.ClaimType, "adventures.quests"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ActiveCompanionControlsLiveInTheSelectedHeaderCardAndUseItsLeader()
    {
        handler.ReturnRememberedSession = true;
        SelectHeroes("Vynlan", "Kiesh");
        var component = Render<AzerothCore_UI.Web.Components.Shared.RealmRosterHeader>();
        component.WaitForElement(".companion-header-controls");
        component.FindAll(".companion-tabs .nav-link")
            .Single(tab => tab.TextContent.Contains("Maintenance"))
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains(
                "api/server-administration/questing-companions/Vynlan/Kiesh/logistics",
                handler.LogisticsPaths);
            Assert.DoesNotContain(
                handler.LogisticsPaths,
                path => path.Contains("/LeaderName/", StringComparison.Ordinal));

            Assert.Single(component.FindAll(".companion-header-controls"));
            Assert.Empty(component.FindAll(".character-picker"));
        });
    }

    [Fact]
    public void HeaderSelectionDrivesTheLeaderAndMultipleOfflineCompanions()
    {
        SelectHeroes("Vynlan", "Highalpha", "Lowzeta");
        var component = Render<QuestingCompanions>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Vynlan", component.Find(".header-party-member.leader").TextContent);
            Assert.Contains("Highalpha", component.Find(".header-companion-lineup").TextContent);
            Assert.Contains("Lowzeta", component.Find(".header-companion-lineup").TextContent);
            Assert.Equal(2, component.FindAll(".header-party-member.ready").Count);
            Assert.False(component.Find(".companion-action").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void ClickingOfflineHeroesInTheSharedHeaderSelectsCompanionsOnThePage()
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/questing-companions");
        var page = Render<QuestingCompanions>();
        var header = Render<AzerothCore_UI.Web.Components.Shared.RealmRosterHeader>();

        header.WaitForElement(".hero-choice");
        HeaderChoice(header, "Vynlan").Click();
        HeaderChoice(header, "Highalpha").Click();
        HeaderChoice(header, "Lowzeta").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Vynlan", page.Find(".header-party-member.leader").TextContent);
            Assert.Equal(2, page.FindAll(".header-party-member.ready").Count);
            Assert.Contains("Highalpha", page.Find(".header-companion-lineup").TextContent);
            Assert.Contains("Lowzeta", page.Find(".header-companion-lineup").TextContent);
            Assert.False(page.Find(".companion-action").HasAttribute("disabled"));
        });
    }

    private static AngleSharp.Dom.IElement HeaderChoice(
        IRenderedComponent<AzerothCore_UI.Web.Components.Shared.RealmRosterHeader> header,
        string name) => header.FindAll(".hero-choice").Single(choice =>
            choice.TextContent.Contains(name, StringComparison.Ordinal));

    private void SelectHeroes(params string[] names) =>
        Services.GetRequiredService<SelectedCharacterStore>()
            .SetSelectedAsync(names, names.FirstOrDefault()).AsTask()
            .GetAwaiter().GetResult();

    [Fact]
    public void RestoresTheRememberedOnlinePartyForTheSignedInUser()
    {
        handler.ReturnRememberedSession = true;

        var component = Render<QuestingCompanions>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Vynlan", component.Find(".remembered-party.selected").TextContent);
            Assert.Contains("Kiesh", component.Markup);
            Assert.Contains(
                "api/server-administration/questing-companions/Vynlan",
                handler.RequestedPaths);
        });
    }

    private sealed class CompanionHandler : HttpMessageHandler
    {
        public List<string> LogisticsPaths { get; } = [];
        public List<string> RequestedPaths { get; } = [];
        public bool ReturnRememberedSession { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            RequestedPaths.Add(path);
            if (request.Method == HttpMethod.Get && path == "api/realm-roster")
                return Task.FromResult(Json(ReturnRememberedSession
                    ? RememberedRoster()
                    : SelectableRoster()));

            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/players")
                return Task.FromResult(Json(new[]
                {
                    new AdministrationPlayer
                    {
                        Name = "Vynlan", Username = "MARK2", Online = true,
                        Classification = "Human"
                    },
                    new AdministrationPlayer
                    {
                        Name = "Kiesh", Username = "MARK", Online = true,
                        Classification = "Human"
                    }
                }));

            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/questing-companions/Vynlan")
                return Task.FromResult(Json(Status()));

            if (request.Method == HttpMethod.Get
                && path.EndsWith("/Kiesh/logistics", StringComparison.Ordinal))
            {
                LogisticsPaths.Add(path);
                return Task.FromResult(Json(new CompanionLogisticsConfiguration(
                    "Kiesh", new(4, 8, false), [], [], [])));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static QuestingCompanionStatus Status() => new(
            "Vynlan",
            [new ActiveQuestingCompanion(
                "Kiesh", 7, 2, true, true, 24, 56, [], "Ready", true, true,
                [], [], [], [],
                new("questing", "damage", "follow", "assist", 3,
                    true, true, true, true),
                new(4, 8, false, 0, "Ready"))],
            [new QuestingCompanionCandidate
                {
                    Name = "Sameaccount", Username = "MARK2", AccountId = 2,
                    Level = 7, CharacterClass = 9, Race = 10, Online = false,
                    SameFaction = true, SameAccount = true
                },
                new QuestingCompanionCandidate
                {
                    Name = "Highalpha", Username = "ALPHA", AccountId = 3,
                    Level = 10, CharacterClass = 8, Race = 10, Online = false,
                    SameFaction = true, SameGuild = true
                },
                new QuestingCompanionCandidate
                {
                    Name = "Lowzeta", Username = "ZETA", AccountId = 4,
                    Level = 5, CharacterClass = 3, Race = 10, Online = false,
                    SameFaction = true, SameGuild = true
                },
                new QuestingCompanionCandidate
                {
                    Name = "Lowalpha", Username = "ALPHA", AccountId = 3,
                    Level = 5, CharacterClass = 5, Race = 10, Online = false,
                    SameFaction = true, SameGuild = true
                }], [], 6);

        private static RealmRosterSnapshot RememberedRoster()
        {
            var companion = new RealmRosterCharacter(
                2, "Kiesh", "MARK", 7, 2, 10, true, false, true);
            var session = new CompanionPartySession(
                1, "Vynlan", 2, "MARK2", true, 1, "owner",
                DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow, 5, [companion]);
            return new RealmRosterSnapshot(
                DateTime.UtcNow,
                [new("remembered:Vynlan", null, "Vynlan", true, true, 5,
                    DateTime.UtcNow,
                    [new(1, "Vynlan", "MARK2", 7, 9, 10, true, true, false),
                     companion])],
                [], [session]);
        }

        private static RealmRosterSnapshot SelectableRoster() => new(
            DateTime.UtcNow, [], [], [],
            [
                new(1, "Vynlan", "MARK2", 7, 9, 10, true, false, false),
                new(3, "Highalpha", "ALPHA", 10, 8, 10, false, false, false),
                new(4, "Lowzeta", "ZETA", 5, 3, 10, false, false, false),
                new(5, "Lowalpha", "ALPHA", 5, 5, 10, false, false, false)
            ]);

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
