using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Services;

public sealed class RosterPreferenceStore(
    IJSRuntime javascript,
    AuthenticationStateProvider authenticationStateProvider)
{
    public async ValueTask<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        try
        {
            var value = await javascript.InvokeAsync<string?>(
                "localStorage.getItem", await KeyAsync(key));
            return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
            return defaultValue;
        }
    }

    public async ValueTask SetBoolAsync(string key, bool value)
    {
        try
        {
            await javascript.InvokeVoidAsync(
                "localStorage.setItem", await KeyAsync(key), value.ToString());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
            // The in-memory default still applies if storage is unavailable.
        }
    }

    private async Task<string> KeyAsync(string key)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return $"azerothcore-ui:roster-preference:{userId}:{key}";
    }
}

public static class RosterPreferenceKeys
{
    public const string AutoReviveCompanions = "auto-revive-companions";
}
