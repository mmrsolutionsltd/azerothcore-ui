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

public sealed class CompanionCommandsTests : BunitContext
{
    private readonly CommandHandler handler = new();

    public CompanionCommandsTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        Services.AddScoped<RecentPickerSelectionStore>();
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
    public void SendsTheTypedCommandToMultipleActiveCompanions()
    {
        SelectHeroes("Kiesh", "Elfruid", "Gennik");
        var component = Render<CompanionCommands>();
        component.WaitForAssertion(() => Assert.Contains("Send to 2", component.Markup));
        component.Find("#companion-command")
            .Input("give Kiesh Linen Cloth 5");
        component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Send to 2")
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, handler.Commands.Count);
            Assert.All(handler.Commands, command =>
            {
                Assert.Equal("Kiesh", command.LeaderName);
                Assert.Equal("give Kiesh Linen Cloth 5", command.Command);
            });
            Assert.Equal(["Elfruid", "Gennik"],
                handler.Commands.Select(command => command.CompanionName).ToArray());
            Assert.Contains("Command sent to 2 companions", component.Markup);
            Assert.Contains("give Kiesh Linen Cloth 5", component.Markup);
            Assert.Contains("Elfruid", component.Markup);
            Assert.Contains("Gennik", component.Markup);
        });
    }

    [Fact]
    public async Task SharedHeaderSelectionUpdatesVisibleCommandTargets()
    {
        SelectHeroes("Kiesh", "Elfruid");
        var component = Render<CompanionCommands>();
        component.WaitForAssertion(() =>
        {
            Assert.Contains("Send to 1", component.Markup);
            Assert.Contains("Elfruid", component.Find(".selected-command-targets").TextContent);
            Assert.Empty(component.FindAll(".character-picker"));
        });

        await Services.GetRequiredService<SelectedCharacterStore>()
            .AddAsync("Gennik");

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Send to 2", component.Markup);
            Assert.Contains("Gennik", component.Find(".selected-command-targets").TextContent);
        });
    }

    [Fact]
    public void PromotesARecentCommandToUsefulExamples()
    {
        SelectHeroes("Kiesh", "Elfruid");
        var component = Render<CompanionCommands>();
        component.WaitForAssertion(() => Assert.Contains("Send to 1", component.Markup));
        component.Find("#companion-command").Input("mail take *");
        component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Send to 1")
            .Click();

        component.WaitForElement("button[title='Add to useful examples']").Click();

        component.WaitForAssertion(() => Assert.Single(
            component.FindAll("button[title='Remove from useful examples']")));
    }

    [Fact]
    public void TradesAnExactLiveInventoryItemAndRefreshesInventory()
    {
        SelectHeroes("Kiesh", "Elfruid");
        var component = Render<CompanionCommands>();
        component.WaitForAssertion(() => Assert.NotEmpty(
            component.FindAll("#trade-item option")));

        component.Find("#trade-companion").Change("Elfruid");
        component.Find("#trade-recipient").Change("Kiesh");
        component.Find("#trade-item").Change("98765");
        component.Find("#trade-quantity").Change("1");
        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Trade").Click();

        component.WaitForAssertion(() =>
        {
            var trade = Assert.Single(handler.Trades);
            Assert.Equal("Kiesh", trade.LeaderName);
            Assert.Equal("Elfruid", trade.CompanionName);
            Assert.Equal("Kiesh", trade.RecipientName);
            Assert.Equal(98765UL, trade.ItemGuid);
            Assert.Equal((uint)3001, trade.ItemId);
            Assert.Equal(1, trade.Quantity);
            Assert.True(handler.CompanionStatusRequests >= 2);
        });
    }

    private void SelectHeroes(params string[] names) =>
        Services.GetRequiredService<SelectedCharacterStore>()
            .SetSelectedAsync(names, names.FirstOrDefault()).AsTask()
            .GetAwaiter().GetResult();

    private sealed class CommandHandler : HttpMessageHandler
    {
        public List<QuestingCompanionCommandRequest> Commands { get; } = [];
        public List<QuestingCompanionTradeRequest> Trades { get; } = [];
        public int CompanionStatusRequests { get; private set; }

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
                    },
                    new AdministrationPlayer
                    {
                        Name = "Sarenilou", Username = "SARA", Online = true,
                        Classification = "Human"
                    }
                });

            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/questing-companions/Kiesh")
            {
                CompanionStatusRequests++;
                return Json(Status());
            }

            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/parties/Kiesh")
                return Json(new PartySnapshot(
                    "Kiesh", 3,
                    [new("Kiesh", 16, "damage", false),
                     new("Elfruid", 16, "damage", true),
                     new("Gennik", 16, "damage", true)], []));

            if (request.Method == HttpMethod.Post
                && path == "api/server-administration/questing-companions/command")
            {
                var command = await request.Content!
                    .ReadFromJsonAsync<QuestingCompanionCommandRequest>(
                        cancellationToken: cancellationToken);
                Assert.NotNull(command);
                Commands.Add(command);
                return Json(new AdministrationResult(
                    true, $"Command sent to {command.CompanionName}.", "Queued"));
            }

            if (request.Method == HttpMethod.Post
                && path == "api/server-administration/questing-companions/trade")
            {
                var trade = await request.Content!
                    .ReadFromJsonAsync<QuestingCompanionTradeRequest>(
                        cancellationToken: cancellationToken);
                Assert.NotNull(trade);
                Trades.Add(trade);
                return Json(new AdministrationResult(
                    true, "Item placed in the trade window.", "Placed"));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static QuestingCompanionStatus Status() => new(
            "Kiesh",
            [Companion("Elfruid", 11, true), Companion("Gennik", 5, false)],
            [], [], 7);

        private static ActiveQuestingCompanion Companion(
            string name, int characterClass, bool inventory) => new(
                name, 16, characterClass, true, true, 19, 56, [], "Ready", true, true,
                [], inventory
                    ? [new QuestingCompanionItem(
                        "bag", 19, 2, 3001, 2, 3, 24, 0, 0, false,
                        "Gold-flecked Gloves")
                        {
                            ItemGuid = 98765,
                            RequiredLevel = 14,
                            Tradeable = true,
                            TemporaryBopTradeable = true
                        }]
                    : [], [], [],
                new("questing", "damage", "follow", "assist", 3,
                    true, true, true, true),
                new(4, 8, false, 0, "Ready"));

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
