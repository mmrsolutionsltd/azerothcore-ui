using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Services;

public sealed class SelectedCharacterStore(
    IJSRuntime javascript,
    AuthenticationStateProvider authenticationStateProvider)
{
    public async ValueTask<string?> GetAsync()
    {
        try
        {
            return await javascript.InvokeAsync<string?>(
                "localStorage.getItem", await KeyAsync());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
            return null;
        }
    }

    public async ValueTask SetAsync(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return;
        try
        {
            await javascript.InvokeVoidAsync(
                "localStorage.setItem", await KeyAsync(), characterName);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
            // Navigation can dispose the circuit while storage is updating.
        }
    }

    private async Task<string> KeyAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return $"azerothcore-ui:selected-character:{userId}";
    }
}
