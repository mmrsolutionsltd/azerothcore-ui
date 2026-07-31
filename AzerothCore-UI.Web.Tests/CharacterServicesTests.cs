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

public sealed class CharacterServicesTests : BunitContext
{
    private readonly CharacterServicesHandler handler = new();

    public CharacterServicesTests()
    {
        Services.AddSingleton(new AccountsApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }));
        Services.AddScoped<SelectedCharacterStore>();
        var authorization = AddAuthorization();
        authorization.SetAuthorized("owner");
        authorization.SetPolicies("players.services");
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(
                AdministrationPermissions.ClaimType,
                "players.services"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AccountTransferRequiresOneCharacterAndConfirmsTheDestination()
    {
        var component = Render<CharacterServices>();
        component.WaitForElement("#transfer-account");

        component.FindAll(".character-picker-item")
            .Single(item => item.TextContent.Contains("Hundead"))
            .Click();
        component.Find("#transfer-account").Change("2");

        var openConfirmation = component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Transfer character");
        Assert.False(openConfirmation.HasAttribute("disabled"));
        openConfirmation.Click();

        component.FindAll("button")
            .Single(button =>
                button.TextContent.Trim() == "Create backup and transfer")
            .Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotNull(handler.TransferRequest);
            Assert.Equal("Hundead", handler.TransferRequest.PlayerName);
            Assert.Equal((uint)2, handler.TransferRequest.DestinationAccountId);
            Assert.True(handler.TransferRequest.Confirmed);
            Assert.Contains(
                "Verified backup test-backup",
                component.Find(".alert-success").TextContent);
        });
    }

    private sealed class CharacterServicesHandler : HttpMessageHandler
    {
        public CharacterAccountTransferRequest? TransferRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/status")
                return Json(new ServerStatus(
                    new("worldserver", true, 1, DateTime.UtcNow, 1),
                    new("authserver", true, 2, DateTime.UtcNow, 1),
                    true,
                    true,
                    "Online",
                    [],
                    new(1, 0, 1),
                    100));
            if (request.Method == HttpMethod.Get
                && path == "api/server-administration/players")
                return Json(new[]
                {
                    new AdministrationPlayer
                    {
                        Name = "Hundead",
                        Username = "OWNER",
                        Online = true,
                        Classification = "Human"
                    }
                });
            if (request.Method == HttpMethod.Get
                && path
                    == "api/server-administration/characters/service/transfer-accounts")
                return Json(new[]
                {
                    new CharacterTransferAccount(1, "OWNER", "Human", 1),
                    new CharacterTransferAccount(2, "FAMILY", "Human", 2)
                });
            if (request.Method == HttpMethod.Post
                && path == "api/server-administration/characters/service/transfer")
            {
                TransferRequest =
                    await request.Content!.ReadFromJsonAsync<
                        CharacterAccountTransferRequest>(
                        cancellationToken: cancellationToken);
                return Json(new AdministrationResult(
                    true,
                    "Hundead moved to FAMILY. Verified backup test-backup was created first."));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value)
            };
    }
}
