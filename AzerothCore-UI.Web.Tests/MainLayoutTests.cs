using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AzerothCore_UI.Web.Clients;
using AzerothCore_UI.Web.Components.Layout;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Security;
using AzerothCore_UI.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(new PermissiveHandler())
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        Services.AddScoped<RosterPreferenceStore>();
        Services.AddScoped<RecentPickerSelectionStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.actions");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, "owner"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void HomeRouteRendersBodyDirectlyWithoutTheToolDrawer()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/");

        var layout = RenderLayout("<p>Home content</p>");

        Assert.Empty(layout.FindAll(".tool-drawer"));
        Assert.Contains("Home content", layout.Markup);
    }

    [Fact]
    public void NonHomeRouteWrapsTheBodyInTheToolDrawer()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/mounts");

        var layout = RenderLayout("<p>Mounts page content</p>");

        Assert.Single(layout.FindAll(".tool-drawer"));
        Assert.Contains("Mounts page content", layout.Find(".tool-drawer").TextContent);
    }

    [Fact]
    public void ClosingTheDrawerNavigatesBackToTheHomeRoute()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("http://localhost/mounts");

        var layout = RenderLayout("<p>Mounts page content</p>");
        layout.Find(".tool-drawer-close").Click();

        Assert.Equal("http://localhost/", navigation.Uri);
    }

    private IRenderedComponent<MainLayout> RenderLayout(string bodyMarkup) =>
        Render<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddMarkupContent(1, bodyMarkup);
                builder.CloseElement();
            })));

    private sealed class PermissiveHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object body = request.RequestUri!.AbsolutePath switch
            {
                "/api/realm-roster" => new RealmRosterSnapshot(DateTime.UtcNow, [], [], [], []),
                "/api/characters" => Array.Empty<CharacterOverviewSummary>(),
                "/api/server-administration/players" => Array.Empty<AdministrationPlayer>(),
                "/api/server-administration/availability" => new ToolAvailability(false, false, false),
                _ => throw new InvalidOperationException(
                    $"Unexpected HTTP request in MainLayout test: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body)
            });
        }
    }
}
