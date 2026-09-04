using System.Security.Claims;
using AzerothCore_UI.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Xunit;

namespace AzerothCore_UI.Web.Tests;

public sealed class RosterPreferenceStoreTests
{
    [Fact]
    public async Task DefaultsToFalseWhenNothingIsStored()
    {
        var store = new RosterPreferenceStore(
            new LocalStorageJavascriptRuntime(), new TestAuthenticationStateProvider("owner"));

        Assert.False(await store.GetBoolAsync(RosterPreferenceKeys.AutoReviveCompanions));
    }

    [Fact]
    public async Task RemembersAStoredValueAcrossReads()
    {
        var javascript = new LocalStorageJavascriptRuntime();
        var store = new RosterPreferenceStore(
            javascript, new TestAuthenticationStateProvider("owner"));

        await store.SetBoolAsync(RosterPreferenceKeys.AutoReviveCompanions, true);

        Assert.True(await store.GetBoolAsync(RosterPreferenceKeys.AutoReviveCompanions));
    }

    [Fact]
    public async Task ScopesStoredValuesPerSignedInUser()
    {
        var javascript = new LocalStorageJavascriptRuntime();
        await new RosterPreferenceStore(javascript, new TestAuthenticationStateProvider("mark"))
            .SetBoolAsync(RosterPreferenceKeys.AutoReviveCompanions, true);

        var micky = new RosterPreferenceStore(
            javascript, new TestAuthenticationStateProvider("micky"));

        Assert.False(await micky.GetBoolAsync(RosterPreferenceKeys.AutoReviveCompanions));
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
