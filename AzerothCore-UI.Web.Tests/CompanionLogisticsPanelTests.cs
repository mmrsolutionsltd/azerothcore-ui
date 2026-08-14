using System.Net;
using System.Net.Http.Json;
using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Components.Shared;
using AzerothCore_UI.Web.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class CompanionLogisticsPanelTests : BunitContext
{
    private readonly LogisticsHandler handler = new();

    public CompanionLogisticsPanelTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
    }

    [Fact]
    public void PreviewUsesCurrentPolicyWithoutSavingOrProcessingItems()
    {
        var component = Render<CompanionLogisticsPanel>(parameters => parameters
            .Add(panel => panel.LeaderName, "Leader")
            .Add(panel => panel.Companion, Companion()));
        component.WaitForElement("button");

        component.FindAll("button").Single(button =>
            button.TextContent.Trim() == "Preview cleanup").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(handler.PreviewRequest);
            Assert.False(handler.SaveWasCalled);
            Assert.Contains("Silk Cloth", component.Markup);
            Assert.Contains("Mail", component.Markup);
            Assert.Contains("Tailor", component.Markup);
            Assert.Contains("wait until the companion is beside a mailbox",
                component.Markup);
        });
    }

    [Fact]
    public void CompanionManagementUsesTabsAndShowsTheEffectiveBagRule()
    {
        var component = Render<CompanionManagementPanel>(parameters => parameters
            .Add(panel => panel.LeaderName, "Leader")
            .Add(panel => panel.Companion, Companion())
            .Add(panel => panel.ProtocolVersion, 5));

        var tabs = component.FindAll(".companion-tabs .nav-link");
        Assert.Equal(3, tabs.Count);
        Assert.Contains(tabs, tab => tab.TextContent.Contains("Behaviour"));
        Assert.Contains(tabs, tab => tab.TextContent.Contains("Inventory"));
        Assert.Contains(tabs, tab => tab.TextContent.Contains("Maintenance"));

        tabs.Single(tab => tab.TextContent.Contains("Inventory")).Click();

        component.WaitForAssertion(() =>
        {
            var row = component.FindAll(".companion-inventory-table tbody tr")
                .Single();
            Assert.Contains("Silk Cloth", row.TextContent);
            Assert.Contains("20", row.TextContent);
            Assert.Contains("10", row.TextContent);
            Assert.Contains("Mail", row.TextContent);
            Assert.Contains("Tailor", row.TextContent);
        });
    }

    private static ActiveQuestingCompanion Companion() => new(
        "Helper", 20, 5, true, true, 2, 36, [], "Ready", true, true,
        [],
        [new("bag", 19, 3, 4306, 20, 1, 10, 0, 0, false, "Silk Cloth")],
        [], [],
        new QuestingCompanionBehavior(
            "questing", "damage", "follow", "assist", 3, true, true,
            true, true),
        new QuestingCompanionLogisticsStatus(
            4, 8, true, 1, "Waiting for bag pressure."));

    private sealed class LogisticsHandler : HttpMessageHandler
    {
        public SaveCompanionLogisticsRequest? PreviewRequest { get; private set; }
        public bool SaveWasCalled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (request.Method == HttpMethod.Get
                && path.EndsWith("/logistics", StringComparison.Ordinal))
                return Json(new CompanionLogisticsConfiguration(
                    "Helper", new(4, 8, true),
                    [new("cloth", "Cloth", 10, "Tailor", 20, true)],
                    [new("cloth", "Cloth", "Cloth materials", 20, [10])],
                    [new(10, "Tailor", "ACCOUNT", ["Tailoring"])]));

            if (request.Method == HttpMethod.Post
                && path.EndsWith("/logistics/preview", StringComparison.Ordinal))
            {
                PreviewRequest = await request.Content!
                    .ReadFromJsonAsync<SaveCompanionLogisticsRequest>(
                        cancellationToken: cancellationToken);
                return Json(new CompanionLogisticsPreview(
                    "Helper", 2, 36, 3, 30, false, false,
                    [new(4306, 20, 1, 19, 3, "Silk Cloth", "Mail", "Tailor",
                        "Matches the cloth route above its configured reserve.")]));
            }

            if (request.Method == HttpMethod.Post
                && path.EndsWith("/logistics", StringComparison.Ordinal))
                SaveWasCalled = true;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };
    }
}
