using AzerothCore_UI.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AzerothCore_UI.Web.Components.Shared;

public partial class TrainerFinderTool : IDisposable
{
    [Parameter] public string CharacterName { get; set; } = "";
    [Parameter] public string? InitialCategory { get; set; }
    [Parameter] public string? InitialSearch { get; set; }

    private TrainerSearchResult results = new([], 1, 30, 0, 0);
    private TrainerSpawn? selectedTrainer;
    private ToolAvailability? availability;
    private CancellationTokenSource? searchCancellation;
    private string loadedCharacter = "";
    private string search = "";
    private string category = "all";
    private string? message;
    private bool initialized;
    private bool isLoading;
    private bool isWorking;
    private bool operationSucceeded;
    private bool confirmed;
    private long queryGeneration;

    private bool CanTeleport =>
        availability is { WorldServerRunning: true, SoapConfigured: true, SoapReachable: true }
        && !isWorking;
    private bool CanTeleportSelected =>
        CanTeleport && confirmed && selectedTrainer is not null
        && !string.IsNullOrWhiteSpace(CharacterName);

    protected override async Task OnParametersSetAsync()
    {
        if (!initialized)
        {
            initialized = true;
            category = InitialCategory is "class" or "profession" or "weapon" or "riding" or "stable"
                ? InitialCategory
                : "all";
            search = InitialSearch?.Trim() ?? "";
            try
            {
                availability = await AccountsClient.GetToolAvailabilityAsync();
            }
            catch (Exception exception)
            {
                operationSucceeded = false;
                message = exception.Message;
            }
        }

        if (loadedCharacter.Equals(CharacterName, StringComparison.OrdinalIgnoreCase))
            return;

        loadedCharacter = CharacterName;
        selectedTrainer = null;
        confirmed = false;
        results = new([], 1, 30, 0, 0);
        if (!string.IsNullOrWhiteSpace(CharacterName))
            await LoadAsync(1);
    }

    private async Task SearchChangedAsync(ChangeEventArgs args)
    {
        search = args.Value?.ToString() ?? "";
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        var generation = ++queryGeneration;
        try
        {
            await Task.Delay(250, searchCancellation.Token);
            await LoadAsync(1, generation, searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task ReloadAsync()
    {
        selectedTrainer = null;
        confirmed = false;
        return string.IsNullOrWhiteSpace(CharacterName)
            ? Task.CompletedTask
            : LoadAsync(1);
    }

    private Task LoadAsync(int page) =>
        LoadAsync(page, ++queryGeneration, CancellationToken.None);

    private async Task LoadAsync(
        int page,
        long generation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CharacterName))
            return;
        isLoading = true;
        try
        {
            var response = await AccountsClient.GetTrainersAsync(
                CharacterName, search, category, page, cancellationToken);
            if (generation == queryGeneration)
            {
                results = response;
                message = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation == queryGeneration)
            {
                operationSucceeded = false;
                message = exception.Message;
            }
        }
        finally
        {
            if (generation == queryGeneration)
                isLoading = false;
        }
    }

    private void SelectTrainer(TrainerSpawn trainer)
    {
        selectedTrainer = trainer;
        confirmed = false;
        message = null;
    }

    /// <summary>Lets a host component (e.g. the hero card's Training tab) re-filter this
    /// tool after it has already loaded, such as jumping straight to a discipline's
    /// trainer from a "find trainer" action elsewhere on the page.</summary>
    public Task SetFilterAsync(string newCategory, string newSearch)
    {
        category = newCategory is "class" or "profession" or "weapon" or "riding" or "stable"
            ? newCategory
            : "all";
        search = newSearch;
        selectedTrainer = null;
        confirmed = false;
        return string.IsNullOrWhiteSpace(CharacterName)
            ? Task.CompletedTask
            : LoadAsync(1);
    }

    private async Task TeleportAsync()
    {
        if (!CanTeleportSelected || selectedTrainer is null)
            return;
        isWorking = true;
        try
        {
            var response = await AccountsClient.TeleportToTrainerAsync(
                new(CharacterName, selectedTrainer.SpawnId, confirmed));
            operationSucceeded = response?.Success == true;
            message = response?.Message;
            if (operationSucceeded)
            {
                confirmed = false;
                await LoadAsync(results.Page);
            }
        }
        catch (Exception exception)
        {
            operationSucceeded = false;
            message = exception.Message;
        }
        finally
        {
            isWorking = false;
        }
    }

    private static string CategoryName(string value) => value switch
    {
        "class" => "Class trainer",
        "profession" => "Profession trainer",
        "weapon" => "Weapon master",
        "riding" => "Riding trainer",
        "stable" => "Stable master",
        _ => value
    };

    private static string DistanceText(TrainerSpawn trainer) =>
        trainer.SameMap && trainer.Distance is { } distance
            ? $"{Math.Round(distance):N0} yards away"
            : "Different continent/map";

    private static string? LocationDetail(TrainerSpawn trainer) =>
        (trainer.ZoneId, trainer.AreaId) switch
        {
            (0, 0) => null,
            (_, 0) => $"Zone {trainer.ZoneId}",
            (0, _) => $"Area {trainer.AreaId}",
            _ => $"Zone {trainer.ZoneId}, area {trainer.AreaId}"
        };

    private static string MapName(ushort mapId) => mapId switch
    {
        0 => "Eastern Kingdoms",
        1 => "Kalimdor",
        530 => "Outland",
        571 => "Northrend",
        609 => "Acherus",
        _ => $"Map {mapId}"
    };

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
    }
}
