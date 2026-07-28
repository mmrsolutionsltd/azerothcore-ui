using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Services;

public sealed class DungeonWishlistStore(
    IJSRuntime javascript,
    AuthenticationStateProvider authenticationStateProvider)
{
    public async ValueTask<HashSet<uint>> GetAsync()
    {
        try
        {
            var json = await javascript.InvokeAsync<string?>(
                "localStorage.getItem", await KeyAsync());
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<HashSet<uint>>(json) ?? [];
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async ValueTask SetAsync(IReadOnlyCollection<uint> itemIds)
    {
        try
        {
            await javascript.InvokeVoidAsync(
                "localStorage.setItem", await KeyAsync(),
                JsonSerializer.Serialize(itemIds));
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
        return $"azerothcore-ui:dungeon-wishlist:{userId}";
    }
}
