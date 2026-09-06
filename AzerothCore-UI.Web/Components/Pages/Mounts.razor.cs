using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Pages;

public partial class Mounts : IDisposable
{
    private AdministrationMountSearchResult results = new([], 1, 30, 0, 0);
    private readonly HashSet<string> targetNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AdministrationMount> selectedMounts = [];
    private IReadOnlyList<PlayerActionResult> giveResults = [];
    private CancellationTokenSource? debounce;
    private string search = "";
    private int? minimumLevel;
    private int? maximumLevel;
    private int? minimumSkillRank;
    private string? faction;
    private string? message;
    private bool allowCrossFaction;
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
            await LoadAsync(results.Page);
            StateHasChanged();
        });

    private void OnTargetsChanged(IReadOnlyList<string> names) => _ = InvokeAsync(async () =>
    {
        targetNames.Clear();
        targetNames.UnionWith(names);
        await LoadAsync(results.Page);
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
                search, minimumLevel, maximumLevel, minimumSkillRank, faction, page, targetNames);
            // Keep any already-selected mount's HeroStatuses current even if it isn't
            // on the currently displayed page (e.g. the hero selection changed while
            // browsing a later page) - a mount not present in this response keeps its
            // previously-fetched (now possibly stale) statuses rather than losing them.
            for (var index = 0; index < selectedMounts.Count; index++)
            {
                var refreshed = results.Mounts.FirstOrDefault(mount => mount.ItemId == selectedMounts[index].ItemId);
                if (refreshed is not null) selectedMounts[index] = refreshed;
            }
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

    private bool IsSelected(AdministrationMount mount) =>
        selectedMounts.Any(selected => selected.ItemId == mount.ItemId);

    private void ToggleMountSelection(AdministrationMount mount)
    {
        var index = selectedMounts.FindIndex(selected => selected.ItemId == mount.ItemId);
        if (index >= 0) selectedMounts.RemoveAt(index);
        else selectedMounts.Add(mount);
        giveResults = [];
        message = null;
    }

    private void ClearMountSelection()
    {
        selectedMounts.Clear();
        giveResults = [];
        message = null;
    }

    private static readonly string[] ReputationRankNames =
        ["Hated", "Hostile", "Unfriendly", "Neutral", "Friendly", "Honored", "Revered", "Exalted"];

    private static string ReputationRankLabel(byte rank) =>
        ReputationRankNames[Math.Clamp(rank, (byte)0, (byte)7)];

    private async Task GiveAsync()
    {
        if (selectedMounts.Count == 0 || targetNames.Count == 0 || isGiving) return;
        isGiving = true;
        var mounts = selectedMounts.ToArray();
        var multipleMounts = mounts.Length > 1;
        var collected = new List<PlayerActionResult>();
        try
        {
            foreach (var mount in mounts)
            {
                foreach (var name in targetNames)
                {
                    var status = mount.HeroStatuses.FirstOrDefault(heroStatus =>
                        string.Equals(heroStatus.CharacterName, name, StringComparison.OrdinalIgnoreCase));
                    var mismatch = status?.FactionMismatch == true;
                    var resultLabel = multipleMounts ? $"{name} — {mount.Name}" : name;
                    try
                    {
                        var result = await Api.GiveItemAsync(new GiveItemRequest(
                            name, mount.ItemId, 1, CrossFactionOverride: mismatch && allowCrossFaction));
                        var resultMessage = result?.Message ?? "No response returned.";
                        if (mismatch)
                            resultMessage += " This is a cross-faction mount: it may still be unusable in-game " +
                                "until the character has appropriate riding skill/training, or may be rejected " +
                                "by the server's own faction check when used.";
                        collected.Add(new PlayerActionResult(resultLabel, result?.Success == true, resultMessage));
                    }
                    catch (Exception exception)
                    {
                        collected.Add(new PlayerActionResult(resultLabel, false, exception.Message));
                    }
                }
            }
            giveResults = collected;
            var successCount = collected.Count(result => result.Success);
            succeeded = successCount == collected.Count;
            var summary = $"{mounts.Length} mount{(mounts.Length == 1 ? "" : "s")} to " +
                $"{targetNames.Count} hero{(targetNames.Count == 1 ? "" : "es")} ({collected.Count} total)";
            message = succeeded
                ? $"Gave {summary}."
                : $"Completed {successCount} of {collected.Count} transfers for {summary}.";
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
