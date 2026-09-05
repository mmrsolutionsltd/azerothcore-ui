using System.Security.Claims;
using AzerothCore_UI.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class SelectedCharacterStoreTests
{
    [Fact]
    public void MaximumCharactersIsFifteen()
    {
        Assert.Equal(15, SelectedCharacterStore.MaximumCharacters);
    }

    [Fact]
    public async Task AddAsyncAcceptsUpToFifteenAndThenRefusesASixteenth()
    {
        var store = NewStore();
        for (var index = 1; index <= 15; ++index)
            Assert.True(await store.AddAsync($"Hero{index}"));

        Assert.False(await store.AddAsync("Hero16"));

        var selected = await store.GetSelectedAsync();
        Assert.Equal(15, selected.Count);
        Assert.DoesNotContain("Hero16", selected);
    }

    [Fact]
    public async Task SetSelectedAsyncTruncatesToTheFirstFifteenNamesAndPreservesOrder()
    {
        var store = NewStore();
        var names = Enumerable.Range(1, 20).Select(index => $"Hero{index}").ToArray();

        await store.SetSelectedAsync(names, "Hero1");

        var selected = await store.GetSelectedAsync();
        Assert.Equal(names.Take(15), selected);
    }

    [Fact]
    public async Task SetAsyncEvictsTheOldestSelectionOnceFifteenAreAlreadySelected()
    {
        var store = NewStore();
        for (var index = 1; index <= 15; ++index)
            await store.AddAsync($"Hero{index}");

        await store.SetAsync("Hero16");

        var selected = await store.GetSelectedAsync();
        Assert.Equal(15, selected.Count);
        Assert.DoesNotContain("Hero1", selected);
        Assert.Contains("Hero16", selected);
    }

    private static SelectedCharacterStore NewStore() => new(
        new NoOpJavascriptRuntime(), new TestAuthenticationStateProvider("owner"));

    private sealed class NoOpJavascriptRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = identifier switch
            {
                "localStorage.getItem" => values.GetValueOrDefault((string)args![0]!),
                "localStorage.setItem" => Set(args!),
                "localStorage.removeItem" => Remove(args!),
                _ => throw new InvalidOperationException($"Unexpected JavaScript call: {identifier}")
            };
            return ValueTask.FromResult((TValue?)result!);
        }

        private object? Set(object?[] args)
        {
            values[(string)args[0]!] = (string)args[1]!;
            return null;
        }

        private object? Remove(object?[] args)
        {
            values.Remove((string)args[0]!);
            return null;
        }
    }

    private sealed class TestAuthenticationStateProvider(string userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)], "Tests");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
