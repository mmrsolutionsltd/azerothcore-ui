using System.Security.Claims;
using AzerothCore_UI.Web.Models;
using AzerothCore_UI.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class RecentPickerSelectionStoreTests
{
    [Fact]
    public async Task KeepsFiveUniqueSelectionsInMostRecentOrder()
    {
        var javascript = new LocalStorageJavascriptRuntime();
        var store = new RecentPickerSelectionStore(
            javascript, new TestAuthenticationStateProvider("owner"));

        for (uint itemId = 1; itemId <= 7; ++itemId)
        {
            await store.RememberAsync(
                RecentPickerKeys.Items,
                new AdministrationItem { ItemId = itemId, Name = $"Item {itemId}" },
                item => item.ItemId.ToString());
        }
        await store.RememberAsync(
            RecentPickerKeys.Items,
            new AdministrationItem { ItemId = 5, Name = "Item 5" },
            item => item.ItemId.ToString());

        var restored = await store.GetAsync<AdministrationItem>(
            RecentPickerKeys.Items);

        Assert.Equal([5u, 7u, 6u, 4u, 3u],
            restored.Select(item => item.ItemId).ToArray());
    }

    [Fact]
    public async Task ReplacesStoredSelectionsWhenAUsefulExampleIsRemoved()
    {
        var javascript = new LocalStorageJavascriptRuntime();
        var store = new RecentPickerSelectionStore(
            javascript, new TestAuthenticationStateProvider("owner"));

        await store.SetAsync(
            RecentPickerKeys.CompanionCommandExamples,
            ["follow", "stay", "items"]);
        await store.SetAsync(
            RecentPickerKeys.CompanionCommandExamples,
            ["follow", "items"]);

        var restored = await store.GetAsync<string>(
            RecentPickerKeys.CompanionCommandExamples);
        Assert.Equal(["follow", "items"], restored);
    }

    private sealed class LocalStorageJavascriptRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args) => InvokeAsync<TValue>(
                identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            object? result = identifier switch
            {
                "localStorage.getItem" =>
                    values.GetValueOrDefault((string)args![0]!),
                "localStorage.setItem" => Set(args!),
                _ => throw new InvalidOperationException(
                    $"Unexpected JavaScript call: {identifier}")
            };
            return ValueTask.FromResult((TValue?)result!);
        }

        private object? Set(object?[] args)
        {
            values[(string)args[0]!] = (string)args[1]!;
            return null;
        }
    }

    private sealed class TestAuthenticationStateProvider(string userId)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)], "Tests");
            return Task.FromResult(
                new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
