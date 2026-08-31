using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AzerothCore_UI.Web.Services;

public sealed class SelectedCharacterStore(
    IJSRuntime javascript,
    AuthenticationStateProvider authenticationStateProvider)
{
    public const int MaximumCharacters = 5;

    private readonly List<string> selectedCharacters = [];
    private readonly HashSet<string> excludedTargets = new(StringComparer.OrdinalIgnoreCase);
    private string? current;
    private bool loaded;

    public event Action<string>? SelectionChanged;
    public event Action<IReadOnlyList<string>>? SelectedCharactersChanged;
    public event Action<IReadOnlyList<string>>? TargetsChanged;

    public async ValueTask<string?> GetAsync()
    {
        await EnsureLoadedAsync();
        return current;
    }

    public async ValueTask<IReadOnlyList<string>> GetSelectedAsync()
    {
        await EnsureLoadedAsync();
        return selectedCharacters.ToArray();
    }

    /// <summary>Selected characters currently included as targets for the player-action
    /// tools. Distinct from row membership (<see cref="GetSelectedAsync"/>) - every
    /// selected character is a target by default until individually toggled off.</summary>
    public async ValueTask<IReadOnlyList<string>> GetTargetsAsync()
    {
        await EnsureLoadedAsync();
        return EffectiveTargets();
    }

    public async ValueTask ToggleTargetAsync(string characterName)
    {
        await EnsureLoadedAsync();
        if (!selectedCharacters.Contains(characterName, StringComparer.OrdinalIgnoreCase))
            return;
        if (!excludedTargets.Remove(characterName)) excludedTargets.Add(characterName);
        await PersistAsync();
        TargetsChanged?.Invoke(EffectiveTargets());
    }

    /// <summary>Narrows the target set to just this one selected character, without
    /// touching row membership (unlike <see cref="SetSelectedAsync"/>).</summary>
    public async ValueTask SetOnlyTargetAsync(string characterName)
    {
        await EnsureLoadedAsync();
        if (!selectedCharacters.Contains(characterName, StringComparer.OrdinalIgnoreCase))
            return;
        excludedTargets.Clear();
        excludedTargets.UnionWith(selectedCharacters.Where(name =>
            !name.Equals(characterName, StringComparison.OrdinalIgnoreCase)));
        await PersistAsync();
        TargetsChanged?.Invoke(EffectiveTargets());
    }

    private string[] EffectiveTargets() => selectedCharacters
        .Where(name => !excludedTargets.Contains(name)).ToArray();

    public async ValueTask SetAsync(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return;
        await EnsureLoadedAsync();
        var normalized = characterName.Trim();
        var listChanged = !selectedCharacters.Contains(
            normalized, StringComparer.OrdinalIgnoreCase);
        if (listChanged)
        {
            if (selectedCharacters.Count >= MaximumCharacters)
                selectedCharacters.RemoveAt(0);
            selectedCharacters.Add(normalized);
        }
        await SetCurrentAndPersistAsync(normalized, listChanged);
    }

    public async ValueTask<bool> AddAsync(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return false;
        await EnsureLoadedAsync();
        var normalized = characterName.Trim();
        if (selectedCharacters.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            await SetCurrentAndPersistAsync(normalized, false);
            return true;
        }
        if (selectedCharacters.Count >= MaximumCharacters) return false;
        selectedCharacters.Add(normalized);
        await SetCurrentAndPersistAsync(normalized, true);
        return true;
    }

    public async ValueTask RemoveAsync(string characterName)
    {
        await EnsureLoadedAsync();
        var removed = selectedCharacters.RemoveAll(name => name.Equals(
            characterName, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) return;
        var currentChanged = string.Equals(current, characterName,
            StringComparison.OrdinalIgnoreCase);
        if (currentChanged) current = selectedCharacters.LastOrDefault();
        await PersistAsync();
        SelectedCharactersChanged?.Invoke(selectedCharacters.ToArray());
        if (currentChanged && current is not null) SelectionChanged?.Invoke(current);
    }

    public async ValueTask SetSelectedAsync(
        IEnumerable<string> characterNames, string? activeCharacter = null)
    {
        await EnsureLoadedAsync();
        var names = characterNames.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCharacters).ToArray();
        var listChanged = !selectedCharacters.SequenceEqual(
            names, StringComparer.OrdinalIgnoreCase);
        selectedCharacters.Clear();
        selectedCharacters.AddRange(names);
        var next = activeCharacter is not null && names.Contains(
            activeCharacter, StringComparer.OrdinalIgnoreCase)
            ? names.First(name => name.Equals(activeCharacter,
                StringComparison.OrdinalIgnoreCase))
            : names.LastOrDefault();
        var currentChanged = !string.Equals(current, next,
            StringComparison.OrdinalIgnoreCase);
        current = next;
        await PersistAsync();
        if (listChanged) SelectedCharactersChanged?.Invoke(selectedCharacters.ToArray());
        if (currentChanged && current is not null) SelectionChanged?.Invoke(current);
    }

    private async ValueTask EnsureLoadedAsync()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            var key = await KeyAsync();
            current = await javascript.InvokeAsync<string?>(
                "localStorage.getItem", key);
            var serialized = await javascript.InvokeAsync<string?>(
                "localStorage.getItem", $"{key}:party");
            if (!string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    selectedCharacters.AddRange(
                        (JsonSerializer.Deserialize<string[]>(serialized) ?? [])
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(MaximumCharacters));
                }
                catch (JsonException)
                {
                    // Ignore malformed pre-release browser state.
                }
            }
            if (!string.IsNullOrWhiteSpace(current)
                && !selectedCharacters.Contains(current,
                    StringComparer.OrdinalIgnoreCase))
            {
                if (selectedCharacters.Count >= MaximumCharacters)
                    selectedCharacters.RemoveAt(0);
                selectedCharacters.Add(current);
            }

            var serializedTargets = await javascript.InvokeAsync<string?>(
                "localStorage.getItem", $"{key}:excluded-targets");
            if (!string.IsNullOrWhiteSpace(serializedTargets))
            {
                try
                {
                    excludedTargets.UnionWith(
                        JsonSerializer.Deserialize<string[]>(serializedTargets) ?? []);
                    excludedTargets.IntersectWith(selectedCharacters);
                }
                catch (JsonException)
                {
                    // Ignore malformed pre-release browser state.
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JSDisconnectedException
                or TaskCanceledException)
        {
            // Browser storage is unavailable while prerendering/disconnecting.
        }
    }

    private async ValueTask SetCurrentAndPersistAsync(
        string characterName, bool listChanged)
    {
        var currentChanged = !string.Equals(current, characterName,
            StringComparison.OrdinalIgnoreCase);
        current = characterName;
        await PersistAsync();
        if (listChanged) SelectedCharactersChanged?.Invoke(selectedCharacters.ToArray());
        if (currentChanged) SelectionChanged?.Invoke(characterName);
    }

    private async ValueTask PersistAsync()
    {
        excludedTargets.IntersectWith(selectedCharacters);
        try
        {
            var key = await KeyAsync();
            if (string.IsNullOrWhiteSpace(current))
                await javascript.InvokeVoidAsync("localStorage.removeItem", key);
            else
                await javascript.InvokeVoidAsync("localStorage.setItem", key, current);
            await javascript.InvokeVoidAsync("localStorage.setItem", $"{key}:party",
                JsonSerializer.Serialize(selectedCharacters));
            await javascript.InvokeVoidAsync("localStorage.setItem", $"{key}:excluded-targets",
                JsonSerializer.Serialize(excludedTargets));
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
