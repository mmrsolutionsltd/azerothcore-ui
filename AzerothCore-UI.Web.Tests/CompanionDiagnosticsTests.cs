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

public sealed class CompanionDiagnosticsTests : BunitContext
{
    private readonly DiagnosticsHandler handler = new();

    public CompanionDiagnosticsTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("adventures.quests", "players.services");
        authorization.SetRoles("Owner");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdministrationPermissions.ClaimType, "adventures.quests"),
            new Claim(AdministrationPermissions.ClaimType, "players.services"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ShowsLiveBlockerAndRunsRecoveryAction()
    {
        var component = Render<CompanionDiagnostics>();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Gennik", component.Markup);
            Assert.Contains("The companion has no free bag slots", component.Markup);
            Assert.Contains("Moving to Doom Weed", component.Markup);
            Assert.Contains("A quest object was not usable", component.Markup);
        });

        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Regroup").Click();

        component.WaitForAssertion(() =>
        {
            var request = Assert.Single(handler.Regroups);
            Assert.Equal("Kiesh", request.LeaderName);
            Assert.Equal("Gennik", request.CompanionName);
            Assert.Contains("Gennik regrouped", component.Markup);
        });
    }

    private sealed class DiagnosticsHandler : HttpMessageHandler
    {
        public List<QuestingCompanionResetRequest> Regroups { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/players")
                return Json(new[]
                {
                    new AdministrationPlayer
                    {
                        Name = "Kiesh", Username = "MARK", Online = true,
                        Classification = "Human"
                    }
                });

            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/questing-companions/Kiesh")
                return Json(Status());

            if (request.Method == HttpMethod.Post
                && path == "api/server-administration/questing-companions/regroup")
            {
                var value = await request.Content!
                    .ReadFromJsonAsync<QuestingCompanionResetRequest>(
                        cancellationToken: cancellationToken);
                Assert.NotNull(value);
                Regroups.Add(value);
                return Json(new AdministrationResult(
                    true, "Gennik regrouped.", "Regrouped"));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static QuestingCompanionStatus Status()
        {
            var companion = new ActiveQuestingCompanion(
                "Gennik", 18, 5, true, true, 0, 56, [],
                "Moving to Doom Weed (8m).", true, true,
                [], [], [], [],
                new("questing", "damage", "follow", "assist", 3,
                    true, true, true, true),
                new(4, 8, true, 2, "Waiting for a mailbox."))
            {
                Diagnostics = new(
                    "Gathering", "None", "Moving to Doom Weed (8m).",
                    "The companion has no free bag slots.", 8, true, true,
                    false, true, 1785776500, "Follow behaviour configured.",
                    1785776400, "A quest object was not usable.")
            };
            return new("Kiesh", [companion], [], [], 10);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
