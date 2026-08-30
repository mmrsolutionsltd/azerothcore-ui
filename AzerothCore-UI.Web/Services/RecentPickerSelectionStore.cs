using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Services;

public sealed class RecentPickerSelectionStore(
    IJSRuntime javascript,
    AuthenticationStateProvider authenticationStateProvider)
{
    public const int MaximumSelections = 5;

    public async ValueTask<IReadOnlyList<T>> GetAsync<T>(string pickerKey)
    {
        try
        {
            var json = await javascript.InvokeAsync<string?>(
                "localStorage.getItem", await KeyAsync(pickerKey));
            return string.IsNullOrWhiteSpace(json)
                ? []
                : (JsonSerializer.Deserialize<T[]>(json) ?? [])
                    .Take(MaximumSelections)
                    .ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async ValueTask<IReadOnlyList<T>> RememberAsync<T>(
        string pickerKey,
        T selection,
        Func<T, string> identity)
    {
        var selectedIdentity = identity(selection);
        var updated = new[] { selection }
            .Concat((await GetAsync<T>(pickerKey)).Where(item =>
                !identity(item).Equals(
                    selectedIdentity, StringComparison.OrdinalIgnoreCase)))
            .Take(MaximumSelections)
            .ToArray();
        return await SetAsync(pickerKey, updated);
    }

    public async ValueTask<IReadOnlyList<T>> SetAsync<T>(
        string pickerKey,
        IEnumerable<T> selections)
    {
        var updated = selections.Take(MaximumSelections).ToArray();
        try
        {
            await javascript.InvokeVoidAsync(
                "localStorage.setItem", await KeyAsync(pickerKey),
                JsonSerializer.Serialize(updated));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
            // The in-memory history still works if storage is unavailable.
        }
        return updated;
    }

    private async Task<string> KeyAsync(string pickerKey)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return $"azerothcore-ui:recent-picker:{userId}:{pickerKey}";
    }
}

public static class RecentPickerKeys
{
    public const string Items = "items";
    public const string Locations = "locations";
    public const string Npcs = "npcs";
    public const string Creatures = "creatures";
    public const string CompanionCommands = "companion-commands";
    public const string CompanionCommandExamples = "companion-command-examples";
}
