using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class Mounts : IDisposable
{
    private AdministrationMountSearchResult results = new([], 1, 30, 0, 0);
    private readonly HashSet<string> targetNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PlayerActionResult> giveResults = [];
    private CancellationTokenSource? debounce;
    private AdministrationMount? selected;
    private string search = "";
    private int? minimumLevel;
    private int? maximumLevel;
    private int? minimumSkillRank;
    private string? faction;
    private string? message;
    private bool isLoadingPage = true, isSearching, isGiving, succeeded;

    protected override async Task OnInitializedAsync()
    {
        SelectedCharacterStore.SelectedCharactersChanged += OnSelectedCharactersChanged;
        SelectedCharacterStore.TargetsChanged += OnTargetsChanged;
        try
        {
            targetNames.UnionWith(await SelectedCharacterStore.GetTargetsAsync());
            await LoadAsync(1);
        }
        catch (Exception exception)
        {
            message = exception.Message;
        }
        finally
        {
            isLoadingPage = false;
        }
    }

    // Row membership changes (add/remove/dismiss) can also change the effective
    // target set, so both events refresh from the store rather than reasoning locally -
    // same convention as PlayerActionsSidebar, which this page's give action mirrors.
    private void OnSelectedCharactersChanged(IReadOnlyList<string> names) =>
        _ = InvokeAsync(async () =>
        {
            targetNames.Clear();
            targetNames.UnionWith(await SelectedCharacterStore.GetTargetsAsync());
            StateHasChanged();
        });

    private void OnTargetsChanged(IReadOnlyList<string> names) => _ = InvokeAsync(() =>
    {
        targetNames.Clear();
        targetNames.UnionWith(names);
        StateHasChanged();
    });

    private async Task SearchChangedAsync(ChangeEventArgs args)
    {
        search = args.Value?.ToString() ?? "";
        debounce?.Cancel();
        debounce?.Dispose();
        debounce = new();
        try { await Task.Delay(250, debounce.Token); await LoadAsync(1); }
        catch (OperationCanceledException) { }
    }

    private Task ReloadAsync() => LoadAsync(1);

    private async Task LoadAsync(int page)
    {
        isSearching = true;
        try
        {
            results = await Api.GetMountsAsync(
                search, minimumLevel, maximumLevel, minimumSkillRank, faction, page);
        }
        catch (Exception exception)
        {
            succeeded = false;
            message = exception.Message;
        }
        finally
        {
            isSearching = false;
        }
    }

    private void SelectMount(AdministrationMount mount)
    {
        selected = mount;
        giveResults = [];
        message = null;
    }

    private async Task GiveAsync()
    {
        if (selected is null || targetNames.Count == 0 || isGiving) return;
        isGiving = true;
        var mount = selected;
        var collected = new List<PlayerActionResult>();
        try
        {
            foreach (var name in targetNames)
            {
                try
                {
                    var result = await Api.GiveItemAsync(new GiveItemRequest(name, mount.ItemId, 1));
                    collected.Add(new PlayerActionResult(
                        name, result?.Success == true, result?.Message ?? "No response returned."));
                }
                catch (Exception exception)
                {
                    collected.Add(new PlayerActionResult(name, false, exception.Message));
                }
            }
            giveResults = collected;
            var successCount = collected.Count(result => result.Success);
            succeeded = successCount == collected.Count;
            message = succeeded
                ? $"{mount.Name} was given to all {successCount} selected hero{(successCount == 1 ? "" : "es")}."
                : $"{mount.Name} was given to {successCount} of {collected.Count} selected heroes.";
        }
        finally
        {
            isGiving = false;
        }
    }

    private static string RidingSkillLabel(int requiredSkillRank) => requiredSkillRank switch
    {
        0 => "No riding skill",
        75 => "Apprentice Riding",
        150 => "Journeyman Riding",
        225 => "Expert Riding",
        300 => "Artisan Riding",
        _ => $"Riding skill {requiredSkillRank}"
    };

    private static readonly byte[] KnownClassIds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 11];

    private static string ClassRestrictionLabel(long allowableClass)
    {
        if (allowableClass is -1 or 0) return "All classes";
        var names = KnownClassIds
            .Where(classId => (allowableClass & (1L << (classId - 1))) != 0)
            .Select(classId => CharacterDisplayNames.Class(classId))
            .ToArray();
        return names.Length == 0 ? "All classes" : string.Join(", ", names);
    }

    private static string SourceLabel(AdministrationMount mount)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mount.SourceVendor)) parts.Add($"Vendor: {mount.SourceVendor}");
        if (!string.IsNullOrWhiteSpace(mount.SourceTrainer)) parts.Add($"Trainer: {mount.SourceTrainer}");
        return parts.Count > 0 ? string.Join(" · ", parts) : "Unknown";
    }

    public void Dispose()
    {
        SelectedCharacterStore.SelectedCharactersChanged -= OnSelectedCharactersChanged;
        SelectedCharacterStore.TargetsChanged -= OnTargetsChanged;
        debounce?.Cancel();
        debounce?.Dispose();
    }
}
