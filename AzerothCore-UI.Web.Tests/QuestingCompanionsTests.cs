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
    public void ActiveCompanionUsesTheSelectedLeaderForLogisticsAndIsNotALeaderChoice()
    {
        var component = Render<QuestingCompanions>();
        component.WaitForElement(".character-picker-item");
        component.FindAll(".character-picker-item")
            .Single(item => item.TextContent.Contains("Vynlan"))
            .Click();

        component.WaitForElement(".companion-tabs");
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

            var leaderPicker = component.FindAll(".character-picker").First();
            Assert.Contains("Vynlan", leaderPicker.TextContent);
            Assert.DoesNotContain("Kiesh", leaderPicker.TextContent);

            var companionPicker = component.FindAll(".character-picker").Last();
            Assert.Contains("Sameaccount", companionPicker.TextContent);
        });
    }

    private sealed class CompanionHandler : HttpMessageHandler
    {
        public List<string> LogisticsPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
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
            }], [], 6);

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
