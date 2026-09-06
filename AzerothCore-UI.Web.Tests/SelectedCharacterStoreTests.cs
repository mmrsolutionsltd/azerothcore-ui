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

    [Fact]
    public async Task AFailedFirstLoadAttemptDuringPrerenderIsRetriedRatherThanStrandingTheCircuit()
    {
        // Simulates Blazor Server's static prerender pass: the very first call to any
        // store method happens before JS interop/localStorage is available and throws,
        // exactly like real prerendering. A persisted selection ("Dkarix") already
        // exists in "localStorage" from a previous session - the fix must still pick
        // it up once the real interactive circuit is up, rather than treating the
        // failed prerender attempt as a permanent, empty "already loaded" state.
        var runtime = new FlakyOnceThenWorkingJavascriptRuntime();
        runtime.Seed("azerothcore-ui:selected-character:owner:party", "[\"Dkarix\"]");
        var store = new SelectedCharacterStore(runtime, new TestAuthenticationStateProvider("owner"));

        var duringPrerender = await store.GetTargetsAsync();
        Assert.Empty(duringPrerender);

        var afterInteractiveConnect = await store.GetTargetsAsync();
        Assert.Equal(["Dkarix"], afterInteractiveConnect);
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

    private sealed class FlakyOnceThenWorkingJavascriptRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];
        private bool firstCall = true;

        public void Seed(string key, string value) => values[key] = value;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (firstCall)
            {
                firstCall = false;
                throw new InvalidOperationException(
                    "JavaScript interop calls cannot be issued during server-side prerendering.");
            }
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
